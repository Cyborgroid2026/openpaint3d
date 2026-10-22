//! OpenPaint 3D engine.
//!
//! A document is a straight-alpha RGBA canvas plus a list of 3D objects that
//! stay editable. The display image is composed from the canvas background,
//! the canvas pixels, the rendered 3D layer and a non-destructive lighting
//! effect. Everything is exposed through a C ABI used by the C# front end.

pub mod math;
pub mod mesh;
pub mod scene;

use math::{clamp01, hash, unpack, v3, M3, V3};
use mesh::Mesh;
use scene::{Camera, Light, Obj, Part, Plane, Target};
use std::slice;
use std::sync::Arc;

const MAX_UNDO: usize = 40;

pub const TOOL_MARKER: u32 = 0;
pub const TOOL_CALLIGRAPHY: u32 = 1;
pub const TOOL_OIL: u32 = 2;
pub const TOOL_WATERCOLOR: u32 = 3;
pub const TOOL_PIXEL: u32 = 4;
pub const TOOL_PENCIL: u32 = 5;
pub const TOOL_ERASER: u32 = 6;
pub const TOOL_CRAYON: u32 = 7;
pub const TOOL_SPRAY: u32 = 8;

/// Non-destructive view settings (canvas background + lighting effect).
#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct View {
    pub show_bg: i32,
    pub bg: u32,
    pub light_color: u32,
    pub brightness: f32,
    pub ambient: f32,
    pub light_angle: f32,
    pub filter: u32,
    pub tint: f32,
}

impl Default for View {
    fn default() -> Self {
        View { show_bg: 1, bg: 0xFFFFFFFF, light_color: 0xFFFFFFFF, brightness: 1.0, ambient: 0.35, light_angle: 0.0, filter: 0, tint: 0.0 }
    }
}

impl View {
    fn light(&self) -> Light {
        let (c, _) = unpack(self.light_color);
        Light::from_angle(self.light_angle, c, self.brightness, self.ambient)
    }
}

#[derive(Clone)]
struct Snap {
    w: usize,
    h: usize,
    px: Vec<u8>,
    objs: Vec<Obj>,
    view: View,
    label: u32,
    /// identity of the history step (stays with it through undo / redo)
    serial: u64,
}

struct Stroke {
    tool: u32,
    color: [f32; 3],
    size: f32,
    opacity: f32,
    finish: u32,
    last: (f32, f32),
    carry: f32,
}

#[derive(Clone, Copy)]
struct Rect {
    x0: usize,
    y0: usize,
    x1: usize,
    y1: usize,
}

impl Rect {
    fn union(a: Option<Rect>, b: Rect) -> Rect {
        match a {
            None => b,
            Some(a) => Rect { x0: a.x0.min(b.x0), y0: a.y0.min(b.y0), x1: a.x1.max(b.x1), y1: a.y1.max(b.y1) },
        }
    }
}

pub struct Doc {
    w: usize,
    h: usize,
    px: Vec<u8>,
    objs: Vec<Obj>,
    next_id: u32,
    view: View,
    undo: Vec<Snap>,
    redo: Vec<Snap>,
    // brush stroke state
    base: Vec<u8>,
    mask: Vec<f32>,
    hbuf: Vec<f32>,
    stroke: Option<Stroke>,
    rng: u64,
    // display cache
    disp: Vec<u8>,
    dirty: Option<Rect>,
    layer: Vec<[f32; 4]>,
    layer_id: Vec<u32>,
    layer_dirty: bool,
    layer_ss: usize,
    // object builder / scratch buffers handed to C#
    builder: Vec<Part>,
    out_f32: Vec<f32>,
    out_u8: Vec<u8>,
    perspective: bool,
    serial: u64,
    /// copied 3D objects (survive deleting the originals, as after "cut")
    clip: Vec<Obj>,
}

fn over(dst: &mut [u8], c: [f32; 3], a: f32) {
    // straight-alpha "source over"
    if a <= 0.0 {
        return;
    }
    let a = a.min(1.0);
    let da = dst[3] as f32 / 255.0;
    let oa = a + da * (1.0 - a);
    if oa <= 0.0 {
        return;
    }
    for k in 0..3 {
        let d = dst[k] as f32 / 255.0;
        dst[k] = (((c[k] * a + d * da * (1.0 - a)) / oa) * 255.0 + 0.5).min(255.0) as u8;
    }
    dst[3] = (oa * 255.0 + 0.5) as u8;
}

/// Colour grading used by the effect presets.
fn grade(filter: u32, c: [f32; 3], fx: f32, fy: f32) -> [f32; 3] {
    let [r, g, b] = c;
    let lum = 0.299 * r + 0.587 * g + 0.114 * b;
    let vig = |k: f32| 1.0 - ((fx - 0.5).powi(2) + (fy - 0.5).powi(2)) * k;
    match filter {
        1 => [lum, lum, lum],
        2 => [lum * 1.07 + 0.08, lum * 0.95 + 0.03, lum * 0.75 - 0.04],
        3 => [r * 1.08 + 0.05, g * 1.02 + 0.015, b * 0.85],
        4 => [r * 0.85, g + 0.015, b * 1.1 + 0.055],
        5 => [1.0 - r, 1.0 - g, 1.0 - b],
        6 => [(r - 0.5) * 1.4 + 0.5, (g - 0.5) * 1.4 + 0.5, (b - 0.5) * 1.4 + 0.5],
        7 => {
            let k = vig(1.6);
            [r * k, g * k, b * k]
        }
        8 => {
            let k = vig(1.2);
            [(lum * 0.9 + r * 0.2 + 0.06) * k, (lum * 0.85 + g * 0.15 + 0.03) * k, (lum * 0.6 + b * 0.15) * k]
        }
        9 => {
            let v = ((lum - 0.5) * 1.6 + 0.5).clamp(0.0, 1.0);
            [v, v, v]
        }
        _ => c,
    }
}

impl Doc {
    fn new(w: usize, h: usize) -> Doc {
        let (w, h) = (w.clamp(1, 16384), h.clamp(1, 16384));
        Doc {
            w,
            h,
            px: vec![0; w * h * 4],
            objs: Vec::new(),
            next_id: 1,
            view: View::default(),
            undo: Vec::new(),
            redo: Vec::new(),
            base: Vec::new(),
            mask: Vec::new(),
            hbuf: Vec::new(),
            stroke: None,
            rng: 0x9E37_79B9_7F4A_7C15,
            disp: vec![0; w * h * 4],
            dirty: Some(Rect { x0: 0, y0: 0, x1: w - 1, y1: h - 1 }),
            layer: Vec::new(),
            layer_id: Vec::new(),
            layer_dirty: true,
            layer_ss: 0,
            builder: Vec::new(),
            out_f32: Vec::new(),
            out_u8: Vec::new(),
            perspective: true,
            serial: 0,
            clip: Vec::new(),
        }
    }

    fn rand(&mut self) -> f32 {
        self.rng ^= self.rng << 13;
        self.rng ^= self.rng >> 7;
        self.rng ^= self.rng << 17;
        (self.rng >> 40) as f32 / (1u64 << 24) as f32
    }

    fn full(&self) -> Rect {
        Rect { x0: 0, y0: 0, x1: self.w - 1, y1: self.h - 1 }
    }

    fn mark(&mut self, r: Rect) {
        self.dirty = Some(Rect::union(self.dirty, r));
    }

    fn mark_all(&mut self) {
        let f = self.full();
        self.mark(f);
    }

    fn scene_changed(&mut self) {
        self.layer_dirty = true;
        self.mark_all();
    }

    fn resized(&mut self) {
        // a dirty rectangle left from the old size may reach past the new edges
        self.dirty = None;
        self.disp = vec![0; self.w * self.h * 4];
        self.layer_ss = 0;
        self.scene_changed();
    }

    // ------------------------------------------------------------- history

    fn snap(&self, label: u32) -> Snap {
        Snap { w: self.w, h: self.h, px: self.px.clone(), objs: self.objs.clone(), view: self.view, label, serial: 0 }
    }

    fn push_undo(&mut self, label: u32) {
        let mut s = self.snap(label);
        self.serial += 1;
        s.serial = self.serial;
        self.undo.push(s);
        if self.undo.len() > MAX_UNDO {
            self.undo.remove(0);
        }
        self.redo.clear();
    }

    fn restore(&mut self, s: Snap) {
        let size_changed = s.w != self.w || s.h != self.h;
        self.w = s.w;
        self.h = s.h;
        self.px = s.px;
        self.objs = s.objs;
        self.view = s.view;
        if size_changed {
            self.resized();
        } else {
            self.scene_changed();
        }
    }

    fn undo(&mut self) -> bool {
        match self.undo.pop() {
            Some(s) => {
                let cur = Snap { serial: s.serial, ..self.snap(s.label) };
                self.redo.push(cur);
                self.restore(s);
                true
            }
            None => false,
        }
    }

    fn redo(&mut self) -> bool {
        match self.redo.pop() {
            Some(s) => {
                let cur = Snap { serial: s.serial, ..self.snap(s.label) };
                self.undo.push(cur);
                self.restore(s);
                true
            }
            None => false,
        }
    }

    fn clip(&self, x0: f32, y0: f32, x1: f32, y1: f32) -> Option<Rect> {
        let (ax, bx) = (x0.min(x1).floor().max(0.0), x0.max(x1).ceil().min(self.w as f32 - 1.0));
        let (ay, by) = (y0.min(y1).floor().max(0.0), y0.max(y1).ceil().min(self.h as f32 - 1.0));
        if ax > bx || ay > by {
            return None;
        }
        Some(Rect { x0: ax as usize, y0: ay as usize, x1: bx as usize, y1: by as usize })
    }

    // -------------------------------------------------------------- brushes

    #[allow(clippy::too_many_arguments)]
    fn stroke_begin(&mut self, tool: u32, color: u32, size: f32, opacity: f32, finish: u32, x: f32, y: f32) {
        self.push_undo(if tool == TOOL_ERASER { 2 } else { 1 });
        let (col, _) = unpack(color);
        let opacity = if tool == TOOL_ERASER { 1.0 } else { clamp01(opacity) };
        self.base.clear();
        self.base.extend_from_slice(&self.px);
        self.mask.clear();
        self.mask.resize(self.w * self.h, 0.0);
        self.hbuf.clear();
        self.hbuf.resize(self.w * self.h, 0.0);
        self.stroke = Some(Stroke { tool, color: col, size: size.max(1.0), opacity, finish, last: (x, y), carry: 0.0 });
        self.stamp(x, y);
    }

    fn stroke_to(&mut self, x: f32, y: f32) {
        let (last, size, tool, carry) = match &self.stroke {
            Some(s) => (s.last, s.size, s.tool, s.carry),
            None => return,
        };
        let spacing = match tool {
            TOOL_SPRAY => (size * 0.35).max(1.0),
            TOOL_PIXEL => 1.0,
            _ => (size * 0.12).max(0.75),
        };
        let (dx, dy) = (x - last.0, y - last.1);
        let dist = (dx * dx + dy * dy).sqrt();
        let mut t = spacing - carry;
        while t <= dist {
            let k = t / dist.max(1e-6);
            self.stamp(last.0 + dx * k, last.1 + dy * k);
            t += spacing;
        }
        if let Some(s) = &mut self.stroke {
            s.carry = dist - (t - spacing);
            s.last = (x, y);
        }
    }

    fn stroke_end(&mut self) {
        self.stroke = None;
        self.base = Vec::new();
        self.mask = Vec::new();
        self.hbuf = Vec::new();
    }

    fn stamp(&mut self, cx: f32, cy: f32) {
        let (tool, size, color, opacity, finish) = match &self.stroke {
            Some(s) => (s.tool, s.size, s.color, s.opacity, s.finish),
            None => return,
        };
        let r = size / 2.0;
        let bb = match self.clip(cx - r - 2.0, cy - r - 2.0, cx + r + 2.0, cy + r + 2.0) {
            Some(b) => b,
            None => return,
        };
        let w = self.w;
        if tool == TOOL_SPRAY {
            let n = ((r * r * 0.12) as usize).max(4);
            for _ in 0..n {
                let a = self.rand() * std::f32::consts::TAU;
                let d = self.rand().sqrt() * r;
                let (px, py) = (cx + a.cos() * d, cy + a.sin() * d);
                if px >= 0.0 && py >= 0.0 && (px as usize) < self.w && (py as usize) < self.h {
                    let i = py as usize * w + px as usize;
                    self.mask[i] = 1.0;
                    self.hbuf[i] = self.hbuf[i].max(1.0);
                }
            }
        } else {
            let (ca, sa) = ((-std::f32::consts::FRAC_PI_4).cos(), (-std::f32::consts::FRAC_PI_4).sin());
            for y in bb.y0..=bb.y1 {
                for x in bb.x0..=bb.x1 {
                    let (dx, dy) = (x as f32 + 0.5 - cx, y as f32 + 0.5 - cy);
                    let d = (dx * dx + dy * dy).sqrt();
                    let edge = clamp01(r - d + 0.5);
                    let cov = match tool {
                        TOOL_CALLIGRAPHY => {
                            let (u, v) = (dx * ca - dy * sa, dx * sa + dy * ca);
                            let ry = (r * 0.28).max(0.75);
                            let e = ((u / r).powi(2) + (v / ry).powi(2)).sqrt();
                            clamp01((1.0 - e) * ry + 0.5)
                        }
                        TOOL_OIL => edge * (0.7 + 0.3 * hash(x as i32 / 2, y as i32)),
                        TOOL_WATERCOLOR => {
                            let k = 1.0 - (d / r).powi(2);
                            k.max(0.0) * (0.45 + 0.15 * hash(x as i32 / 3, y as i32 / 3))
                        }
                        TOOL_PIXEL => {
                            let rr = r.max(0.5);
                            if dx.abs() <= rr && dy.abs() <= rr { 1.0 } else { 0.0 }
                        }
                        TOOL_PENCIL => edge * (0.45 + 0.55 * hash(x as i32, y as i32)),
                        TOOL_CRAYON => {
                            if hash(x as i32, y as i32 / 2) > 0.35 { edge } else { edge * 0.15 }
                        }
                        _ => edge,
                    };
                    let i = y * w + x;
                    if cov > self.mask[i] {
                        self.mask[i] = cov;
                    }
                    let hh = if d < r { r * (1.0 - (d / r).powi(2)).sqrt() } else { 0.0 };
                    if hh > self.hbuf[i] {
                        self.hbuf[i] = hh;
                    }
                }
            }
        }
        let light = Light::default();
        let view = v3(0.0, 0.0, -1.0);
        for y in bb.y0..=bb.y1 {
            for x in bb.x0..=bb.x1 {
                let i = y * w + x;
                let m = self.mask[i] * opacity;
                if m <= 0.0 {
                    continue;
                }
                let j = i * 4;
                let mut px = [self.base[j], self.base[j + 1], self.base[j + 2], self.base[j + 3]];
                if tool == TOOL_ERASER {
                    px[3] = (px[3] as f32 * (1.0 - m) + 0.5) as u8;
                } else {
                    let c = if finish == scene::FINISH_MATTE {
                        color
                    } else {
                        let hx = (self.hbuf[if x + 1 < w { i + 1 } else { i }] - self.hbuf[if x > 0 { i - 1 } else { i }]) * 0.5;
                        let hy = (self.hbuf[if y + 1 < self.h { i + w } else { i }] - self.hbuf[if y > 0 { i - w } else { i }]) * 0.5;
                        scene::shade(color, finish, v3(-hx, -hy, -1.0).norm(), view, &light)
                    };
                    over(&mut px, c, m);
                }
                self.px[j..j + 4].copy_from_slice(&px);
            }
        }
        self.mark(bb);
    }

    // ---------------------------------------------------------- fill / pick

    fn fill(&mut self, x: i32, y: i32, color: u32, tol: u32) -> bool {
        if x < 0 || y < 0 || x as usize >= self.w || y as usize >= self.h {
            return false;
        }
        let (c, a) = unpack(color);
        let (w, h) = (self.w, self.h);
        let i0 = (y as usize * w + x as usize) * 4;
        let target = [self.px[i0], self.px[i0 + 1], self.px[i0 + 2], self.px[i0 + 3]];
        let tol = tol.min(255) as i32;
        let matches = |px: &[u8], i: usize| -> bool {
            let j = i * 4;
            if target[3] == 0 {
                return (px[j + 3] as i32) <= tol / 2;
            }
            (0..4).all(|k| (px[j + k] as i32 - target[k] as i32).abs() <= tol)
        };
        self.push_undo(3);
        let mut seen = vec![false; w * h];
        let mut stack = vec![(x as usize, y as usize)];
        let mut region = Vec::new();
        while let Some((sx, sy)) = stack.pop() {
            let row = sy * w;
            if seen[row + sx] || !matches(&self.px, row + sx) {
                continue;
            }
            let mut lx = sx;
            while lx > 0 && !seen[row + lx - 1] && matches(&self.px, row + lx - 1) {
                lx -= 1;
            }
            let mut rx = sx;
            while rx + 1 < w && !seen[row + rx + 1] && matches(&self.px, row + rx + 1) {
                rx += 1;
            }
            for xx in lx..=rx {
                seen[row + xx] = true;
                region.push(row + xx);
                if sy > 0 && !seen[row - w + xx] {
                    stack.push((xx, sy - 1));
                }
                if sy + 1 < h && !seen[row + w + xx] {
                    stack.push((xx, sy + 1));
                }
            }
        }
        for i in region {
            over(&mut self.px[i * 4..i * 4 + 4], c, a);
        }
        self.mark_all();
        true
    }

    /// Displayed colour (after composition) at a canvas position.
    fn pick(&mut self, x: i32, y: i32) -> u32 {
        if x < 0 || y < 0 || x as usize >= self.w || y as usize >= self.h {
            return 0;
        }
        self.update_display(1);
        let i = (y as usize * self.w + x as usize) * 4;
        let d = &self.disp[i..i + 4];
        if d[3] == 0 {
            return self.view.bg | 255;
        }
        (d[0] as u32) << 24 | (d[1] as u32) << 16 | (d[2] as u32) << 8 | 255
    }

    /// Composite a premultiplied BGRA image (text, stickers, 2D shapes).
    #[allow(clippy::too_many_arguments)]
    fn blend_bgra(&mut self, src: &[u8], sw: usize, sh: usize, dx: i32, dy: i32, opacity: f32, finish: u32, label: u32) {
        // label 0 continues the previous step (dropping a lifted selection back onto the canvas);
        // it is still an edit, so whatever could be redone is gone
        if label != 0 {
            self.push_undo(label);
        } else {
            self.redo.clear();
        }
        // height field from blurred alpha for glossy / metallic finishes
        let heights = if finish != scene::FINISH_MATTE {
            let mut a: Vec<f32> = (0..sw * sh).map(|i| src[i * 4 + 3] as f32 / 255.0).collect();
            let r = 4i64;
            for _ in 0..2 {
                let mut t = vec![0.0f32; sw * sh];
                for y in 0..sh {
                    for x in 0..sw {
                        let (mut s, mut n) = (0.0, 0.0);
                        for k in -r..=r {
                            let xx = x as i64 + k;
                            if xx >= 0 && xx < sw as i64 {
                                s += a[y * sw + xx as usize];
                                n += 1.0;
                            }
                        }
                        t[y * sw + x] = s / n;
                    }
                }
                for y in 0..sh {
                    for x in 0..sw {
                        let (mut s, mut n) = (0.0, 0.0);
                        for k in -r..=r {
                            let yy = y as i64 + k;
                            if yy >= 0 && yy < sh as i64 {
                                s += t[yy as usize * sw + x];
                                n += 1.0;
                            }
                        }
                        a[y * sw + x] = s / n;
                    }
                }
            }
            Some(a)
        } else {
            None
        };
        let light = Light::default();
        for y in 0..sh {
            let ty = y as i32 + dy;
            if ty < 0 || ty as usize >= self.h {
                continue;
            }
            for x in 0..sw {
                let tx = x as i32 + dx;
                if tx < 0 || tx as usize >= self.w {
                    continue;
                }
                let s = (y * sw + x) * 4;
                let a = src[s + 3] as f32 / 255.0;
                if a <= 0.0 {
                    continue;
                }
                let mut c = [src[s + 2] as f32 / 255.0 / a, src[s + 1] as f32 / 255.0 / a, src[s] as f32 / 255.0 / a];
                if let Some(hm) = &heights {
                    let i = y * sw + x;
                    let hx = (hm[if x + 1 < sw { i + 1 } else { i }] - hm[if x > 0 { i - 1 } else { i }]) * 6.0;
                    let hy = (hm[if y + 1 < sh { i + sw } else { i }] - hm[if y > 0 { i - sw } else { i }]) * 6.0;
                    c = scene::shade(c, finish, v3(-hx, -hy, -1.0).norm(), v3(0.0, 0.0, -1.0), &light);
                }
                let d = (ty as usize * self.w + tx as usize) * 4;
                over(&mut self.px[d..d + 4], [c[0].min(1.0), c[1].min(1.0), c[2].min(1.0)], a * clamp01(opacity));
            }
        }
        self.mark_all();
    }

    fn load_rgba(&mut self, src: &[u8], w: usize, h: usize) {
        self.w = w;
        self.h = h;
        self.px = src.to_vec();
        self.objs.clear();
        self.undo.clear();
        self.redo.clear();
        self.resized();
    }

    // ---------------------------------------------------------- canvas edits

    fn resize(&mut self, nw: usize, nh: usize, scale: bool) {
        let (nw, nh) = (nw.clamp(1, 16384), nh.clamp(1, 16384));
        self.push_undo(10);
        let mut out = vec![0u8; nw * nh * 4];
        let (ow, oh) = (self.w, self.h);
        if scale {
            let (sx, sy) = (ow as f32 / nw as f32, oh as f32 / nh as f32);
            for y in 0..nh {
                let fy = ((y as f32 + 0.5) * sy - 0.5).max(0.0);
                let (y0, ty) = (fy as usize, fy.fract());
                let y1 = (y0 + 1).min(oh - 1);
                for x in 0..nw {
                    let fx = ((x as f32 + 0.5) * sx - 0.5).max(0.0);
                    let (x0, tx) = (fx as usize, fx.fract());
                    let x1 = (x0 + 1).min(ow - 1);
                    for k in 0..4 {
                        let p = |xx: usize, yy: usize| self.px[(yy * ow + xx) * 4 + k] as f32;
                        let top = p(x0, y0) * (1.0 - tx) + p(x1, y0) * tx;
                        let bot = p(x0, y1) * (1.0 - tx) + p(x1, y1) * tx;
                        out[(y * nw + x) * 4 + k] = (top * (1.0 - ty) + bot * ty + 0.5) as u8;
                    }
                }
            }
            let (kx, ky) = (nw as f32 / ow as f32, nh as f32 / oh as f32);
            let kz = (kx + ky) / 2.0;
            for o in &mut self.objs {
                o.pos = v3(o.pos.x * kx, o.pos.y * ky, o.pos.z * kz);
                o.scale = v3(o.scale.x * kx, o.scale.y * ky, o.scale.z * kz);
            }
        } else {
            let ox = (nw as i64 - ow as i64) / 2;
            let oy = (nh as i64 - oh as i64) / 2;
            for y in 0..oh {
                let ny = y as i64 + oy;
                if ny < 0 || ny >= nh as i64 {
                    continue;
                }
                for x in 0..ow {
                    let nx = x as i64 + ox;
                    if nx < 0 || nx >= nw as i64 {
                        continue;
                    }
                    let (s, d) = ((y * ow + x) * 4, (ny as usize * nw + nx as usize) * 4);
                    out[d..d + 4].copy_from_slice(&self.px[s..s + 4]);
                }
            }
            for o in &mut self.objs {
                o.pos = o.pos + v3(ox as f32, oy as f32, 0.0);
            }
        }
        self.w = nw;
        self.h = nh;
        self.px = out;
        self.resized();
    }

    /// Re-frame the canvas to the rectangle (x, y, cw, ch) given in current
    /// canvas coordinates. Parts outside the old canvas become transparent.
    fn crop(&mut self, x: i32, y: i32, cw: i32, ch: i32) -> bool {
        if cw <= 0 || ch <= 0 || cw > 16384 || ch > 16384 {
            return false;
        }
        self.push_undo(11);
        let (nw, nh) = (cw as usize, ch as usize);
        let mut out = vec![0u8; nw * nh * 4];
        for yy in 0..nh {
            let sy = yy as i64 + y as i64;
            if sy < 0 || sy >= self.h as i64 {
                continue;
            }
            let sx0 = (x as i64).max(0);
            let sx1 = (x as i64 + cw as i64).min(self.w as i64);
            if sx1 <= sx0 {
                continue;
            }
            let n = (sx1 - sx0) as usize;
            let s = (sy as usize * self.w + sx0 as usize) * 4;
            let dx = (sx0 - x as i64) as usize;
            out[(yy * nw + dx) * 4..(yy * nw + dx + n) * 4].copy_from_slice(&self.px[s..s + n * 4]);
        }
        for o in &mut self.objs {
            o.pos = o.pos - v3(x as f32, y as f32, 0.0);
        }
        self.w = nw;
        self.h = nh;
        self.px = out;
        self.resized();
        true
    }

    fn rotate(&mut self, cw: bool) {
        self.push_undo(12);
        let (w, h) = (self.w, self.h);
        let mut out = vec![0u8; w * h * 4];
        for y in 0..h {
            for x in 0..w {
                let (nx, ny) = if cw { (h - 1 - y, x) } else { (y, w - 1 - x) };
                let (s, d) = ((y * w + x) * 4, (ny * h + nx) * 4);
                out[d..d + 4].copy_from_slice(&self.px[s..s + 4]);
            }
        }
        let r = M3::rot_axis(2, if cw { 90.0 } else { -90.0 });
        for o in &mut self.objs {
            o.pos = if cw { v3(h as f32 - o.pos.y, o.pos.x, o.pos.z) } else { v3(o.pos.y, w as f32 - o.pos.x, o.pos.z) };
            o.rot = r.mul(&o.rot);
        }
        self.w = h;
        self.h = w;
        self.px = out;
        self.resized();
    }

    fn flip(&mut self, horizontal: bool) {
        self.push_undo(13);
        let (w, h) = (self.w, self.h);
        let mut out = vec![0u8; w * h * 4];
        for y in 0..h {
            for x in 0..w {
                let (nx, ny) = if horizontal { (w - 1 - x, y) } else { (x, h - 1 - y) };
                let (s, d) = ((y * w + x) * 4, (ny * w + nx) * 4);
                out[d..d + 4].copy_from_slice(&self.px[s..s + 4]);
            }
        }
        let f = if horizontal { M3::scale(v3(-1.0, 1.0, 1.0)) } else { M3::scale(v3(1.0, -1.0, 1.0)) };
        for o in &mut self.objs {
            if horizontal {
                o.pos.x = w as f32 - o.pos.x;
                o.scale.x = -o.scale.x;
            } else {
                o.pos.y = h as f32 - o.pos.y;
                o.scale.y = -o.scale.y;
            }
            o.rot = f.mul(&o.rot).mul(&f);
        }
        self.px = out;
        self.scene_changed();
    }

    // ---------------------------------------------------------------- scene

    fn obj_mut(&mut self, id: u32) -> Option<&mut Obj> {
        self.objs.iter_mut().find(|o| o.id == id)
    }

    fn ensure_layer(&mut self, ss: usize) {
        let ss = ss.clamp(1, 3);
        if !self.layer_dirty && self.layer_ss == ss && self.layer.len() == self.w * self.h {
            return;
        }
        let n = self.w * self.h;
        self.layer.clear();
        self.layer.resize(n, [0.0; 4]);
        self.layer_id.clear();
        self.layer_id.resize(n, 0);
        if !self.objs.is_empty() {
            let (sw, sh) = (self.w * ss, self.h * ss);
            let mut color = vec![[0.0f32; 4]; sw * sh];
            let mut depth = vec![0.0f32; sw * sh];
            let mut id = vec![0u32; sw * sh];
            let mut t = Target { w: sw, h: sh, y0: 0, color: &mut color, depth: &mut depth, id: &mut id };
            let cam = Camera::canvas(self.w as f32, self.h as f32, ss as f32);
            scene::render(&self.objs, &cam, &self.view.light(), None, &mut t);
            let k = 1.0 / (ss * ss) as f32;
            for y in 0..self.h {
                for x in 0..self.w {
                    let mut acc = [0.0f32; 4];
                    for sy in 0..ss {
                        for sx in 0..ss {
                            let c = color[(y * ss + sy) * sw + x * ss + sx];
                            for q in 0..4 {
                                acc[q] += c[q];
                            }
                        }
                    }
                    let i = y * self.w + x;
                    self.layer[i] = [acc[0] * k, acc[1] * k, acc[2] * k, acc[3] * k];
                    self.layer_id[i] = id[(y * ss + ss / 2) * sw + x * ss + ss / 2];
                }
            }
        }
        self.layer_dirty = false;
        self.layer_ss = ss;
    }

    /// Re-compose the dirty part of the display image.
    fn update_display(&mut self, ss: usize) {
        if self.layer_dirty || (self.layer_ss != ss.clamp(1, 3) && !self.objs.is_empty()) {
            self.ensure_layer(ss);
            self.mark_all();
        }
        let r = match self.dirty.take() {
            Some(r) => r,
            None => return,
        };
        let v = self.view;
        let (bg, _) = unpack(v.bg);
        let (lc, _) = unpack(v.light_color);
        let tint = [1.0 + (lc[0] - 1.0) * v.tint, 1.0 + (lc[1] - 1.0) * v.tint, 1.0 + (lc[2] - 1.0) * v.tint];
        let exposure = 0.75 + 0.25 * v.brightness;
        let has_layer = self.layer.len() == self.w * self.h;
        let (w, fw, fh) = (self.w, self.w as f32, self.h as f32);
        let (px, layer) = (&self.px, &self.layer);
        let bg_on = v.show_bg != 0;
        let compose_row = |y: usize, out: &mut [u8]| {
            for x in r.x0..=r.x1 {
                let i = y * w + x;
                let (mut c, mut a) = if bg_on { (bg, 1.0) } else { ([0.0; 3], 0.0) };
                // c is premultiplied from here on
                let p = &px[i * 4..i * 4 + 4];
                let pa = p[3] as f32 / 255.0;
                if pa > 0.0 {
                    for k in 0..3 {
                        c[k] = p[k] as f32 / 255.0 * pa + c[k] * (1.0 - pa);
                    }
                    a = pa + a * (1.0 - pa);
                }
                if has_layer {
                    let l = layer[i];
                    if l[3] > 0.0 {
                        for k in 0..3 {
                            c[k] = l[k] + c[k] * (1.0 - l[3]);
                        }
                        a = l[3] + a * (1.0 - l[3]);
                    }
                }
                let o = &mut out[x * 4..x * 4 + 4];
                if a <= 0.0 {
                    o.copy_from_slice(&[0, 0, 0, 0]);
                    continue;
                }
                let mut s = [c[0] / a, c[1] / a, c[2] / a];
                if v.filter != 0 {
                    s = grade(v.filter, s, (x as f32 + 0.5) / fw, (y as f32 + 0.5) / fh);
                }
                for k in 0..3 {
                    o[k] = (clamp01(s[k] * tint[k] * exposure) * 255.0 + 0.5) as u8;
                }
                o[3] = (a * 255.0 + 0.5) as u8;
            }
        };
        let rows = &mut self.disp[r.y0 * w * 4..(r.y1 + 1) * w * 4];
        let n_rows = r.y1 - r.y0 + 1;
        let threads = if n_rows * (r.x1 - r.x0 + 1) > 64 * 1024 {
            std::thread::available_parallelism().map_or(1, |n| n.get()).min(16)
        } else {
            1
        };
        let per = n_rows.div_ceil(threads);
        std::thread::scope(|sc| {
            for (k, chunk) in rows.chunks_mut(per * w * 4).enumerate() {
                let f = &compose_row;
                let y_start = r.y0 + k * per;
                let mut run = move || {
                    for (j, row) in chunk.chunks_mut(w * 4).enumerate() {
                        f(y_start + j, row);
                    }
                };
                if threads == 1 {
                    run();
                } else {
                    sc.spawn(run);
                }
            }
        });
    }

    fn render_3d(&mut self, out: &mut [u8], ow: usize, oh: usize, yaw: f32, pitch: f32, zoom: f32, ss: usize) {
        let ss = ss.clamp(1, 3);
        // canvas texture: pixels over the background (white when hidden)
        let (bg, _) = unpack(if self.view.show_bg != 0 { self.view.bg } else { 0xFFFFFFFF });
        let mut tex = vec![255u8; self.w * self.h * 4];
        for i in 0..self.w * self.h {
            let a = self.px[i * 4 + 3] as f32 / 255.0;
            for k in 0..3 {
                tex[i * 4 + k] = ((self.px[i * 4 + k] as f32 / 255.0 * a + bg[k] * (1.0 - a)) * 255.0 + 0.5) as u8;
            }
        }
        let (sw, sh) = (ow * ss, oh * ss);
        let mut color = vec![[0.0f32; 4]; sw * sh];
        let mut depth = vec![0.0f32; sw * sh];
        let mut id = vec![0u32; sw * sh];
        let plane_mesh = mesh::canvas_quad(self.w as f32, self.h as f32);
        let plane = Plane { mesh: &plane_mesh, tex: &tex, tw: self.w, th: self.h };
        let cam = Camera::orbit(self.w as f32, self.h as f32, yaw, pitch, zoom, sw as f32, sh as f32, self.perspective);
        let mut t = Target { w: sw, h: sh, y0: 0, color: &mut color, depth: &mut depth, id: &mut id };
        scene::render(&self.objs, &cam, &self.view.light(), Some(&plane), &mut t);
        let k = 1.0 / (ss * ss) as f32;
        for y in 0..oh {
            // background gradient
            let g = 0.86 - 0.18 * (y as f32 / oh as f32);
            for x in 0..ow {
                let mut acc = [0.0f32; 4];
                for sy in 0..ss {
                    for sx in 0..ss {
                        let c = color[(y * ss + sy) * sw + x * ss + sx];
                        for q in 0..4 {
                            acc[q] += c[q] * k;
                        }
                    }
                }
                let o = &mut out[(y * ow + x) * 4..(y * ow + x) * 4 + 4];
                for q in 0..3 {
                    let v = acc[q] + g * (1.0 - acc[3]);
                    o[q] = (clamp01(v) * 255.0 + 0.5) as u8;
                }
                o[3] = 255;
            }
        }
    }

    fn export_tris(&mut self) -> usize {
        self.out_f32.clear();
        for o in &self.objs {
            let ol = o.linear();
            for part in o.parts.iter() {
                let lin = ol.mul(&part.m);
                let nm = lin.normal_matrix();
                let c = part.color.unwrap_or(o.color);
                for t in &part.mesh.tri {
                    let p: Vec<V3> = t.iter().map(|&k| ol.apply(part.m.apply(part.mesh.pos[k as usize]) + part.off) + o.pos).collect();
                    // keep a consistent winding whatever the scale signs are
                    let face = (p[1] - p[0]).cross(p[2] - p[0]);
                    let navg = t.iter().fold(V3::default(), |a, &k| a + nm.apply(part.mesh.nrm[k as usize]));
                    let order: [usize; 3] = if face.dot(navg) < 0.0 { [0, 2, 1] } else { [0, 1, 2] };
                    for &j in &order {
                        let n = nm.apply(part.mesh.nrm[t[j] as usize]).norm();
                        self.out_f32.extend_from_slice(&[p[j].x, p[j].y, p[j].z, n.x, n.y, n.z, c[0], c[1], c[2]]);
                    }
                }
            }
        }
        self.out_f32.len() / 9
    }

    // -------------------------------------------------------- serialisation

    fn save(&mut self) -> usize {
        let mut b: Vec<u8> = Vec::new();
        let u32w = |b: &mut Vec<u8>, v: u32| b.extend_from_slice(&v.to_le_bytes());
        let f32w = |b: &mut Vec<u8>, v: f32| b.extend_from_slice(&v.to_le_bytes());
        b.extend_from_slice(b"OP3D");
        u32w(&mut b, 2);
        u32w(&mut b, self.w as u32);
        u32w(&mut b, self.h as u32);
        b.extend_from_slice(&self.px);
        let v = self.view;
        u32w(&mut b, v.show_bg as u32);
        u32w(&mut b, v.bg);
        u32w(&mut b, v.light_color);
        f32w(&mut b, v.brightness);
        f32w(&mut b, v.ambient);
        f32w(&mut b, v.light_angle);
        u32w(&mut b, v.filter);
        f32w(&mut b, v.tint);
        u32w(&mut b, self.next_id);
        u32w(&mut b, self.objs.len() as u32);
        for o in &self.objs {
            u32w(&mut b, o.id);
            for f in [o.pos.x, o.pos.y, o.pos.z] {
                f32w(&mut b, f);
            }
            for row in o.rot.0 {
                for f in row {
                    f32w(&mut b, f);
                }
            }
            for f in [o.scale.x, o.scale.y, o.scale.z, o.color[0], o.color[1], o.color[2]] {
                f32w(&mut b, f);
            }
            u32w(&mut b, o.finish);
            u32w(&mut b, o.group);
            u32w(&mut b, o.parts.len() as u32);
            for p in o.parts.iter() {
                for row in p.m.0 {
                    for f in row {
                        f32w(&mut b, f);
                    }
                }
                for f in [p.off.x, p.off.y, p.off.z] {
                    f32w(&mut b, f);
                }
                let c = p.color.unwrap_or([-1.0; 3]);
                for f in c {
                    f32w(&mut b, f);
                }
                u32w(&mut b, p.mesh.pos.len() as u32);
                for (q, n) in p.mesh.pos.iter().zip(&p.mesh.nrm) {
                    for f in [q.x, q.y, q.z, n.x, n.y, n.z] {
                        f32w(&mut b, f);
                    }
                }
                u32w(&mut b, p.mesh.tri.len() as u32);
                for t in &p.mesh.tri {
                    for k in t {
                        u32w(&mut b, *k);
                    }
                }
            }
        }
        self.out_u8 = b;
        self.out_u8.len()
    }

    fn load(&mut self, data: &[u8]) -> bool {
        struct R<'a> {
            d: &'a [u8],
            p: usize,
        }
        impl R<'_> {
            fn u(&mut self) -> Option<u32> {
                let v = u32::from_le_bytes(self.d.get(self.p..self.p + 4)?.try_into().ok()?);
                self.p += 4;
                Some(v)
            }
            fn f(&mut self) -> Option<f32> {
                self.u().map(f32::from_bits)
            }
            fn bytes(&mut self, n: usize) -> Option<&[u8]> {
                let s = self.d.get(self.p..self.p + n)?;
                self.p += n;
                Some(s)
            }
        }
        let parse = |d: &[u8]| -> Option<(usize, usize, Vec<u8>, View, u32, Vec<Obj>)> {
            let mut r = R { d, p: 0 };
            if r.bytes(4)? != b"OP3D" {
                return None;
            }
            let version = r.u()?;
            if version != 1 && version != 2 {
                return None;
            }
            let (w, h) = (r.u()? as usize, r.u()? as usize);
            if w == 0 || h == 0 || w > 16384 || h > 16384 {
                return None;
            }
            let px = r.bytes(w * h * 4)?.to_vec();
            let view = View {
                show_bg: r.u()? as i32,
                bg: r.u()?,
                light_color: r.u()?,
                brightness: r.f()?,
                ambient: r.f()?,
                light_angle: r.f()?,
                filter: r.u()?,
                tint: r.f()?,
            };
            let next_id = r.u()?;
            let n = r.u()? as usize;
            let mut objs = Vec::with_capacity(n.min(10_000));
            for _ in 0..n {
                let id = r.u()?;
                let pos = v3(r.f()?, r.f()?, r.f()?);
                let mut rot = [[0.0; 3]; 3];
                for row in &mut rot {
                    for f in row.iter_mut() {
                        *f = r.f()?;
                    }
                }
                let scale = v3(r.f()?, r.f()?, r.f()?);
                let color = [r.f()?, r.f()?, r.f()?];
                let finish = r.u()?;
                let group = if version >= 2 { r.u()? } else { 0 };
                let np = r.u()? as usize;
                let mut parts = Vec::with_capacity(np.min(1000));
                for _ in 0..np {
                    let mut m = [[0.0; 3]; 3];
                    for row in &mut m {
                        for f in row.iter_mut() {
                            *f = r.f()?;
                        }
                    }
                    let off = v3(r.f()?, r.f()?, r.f()?);
                    let c = [r.f()?, r.f()?, r.f()?];
                    let nv = r.u()? as usize;
                    let mut mesh = Mesh::default();
                    for _ in 0..nv {
                        mesh.pos.push(v3(r.f()?, r.f()?, r.f()?));
                        mesh.nrm.push(v3(r.f()?, r.f()?, r.f()?));
                    }
                    let nt = r.u()? as usize;
                    for _ in 0..nt {
                        let t = [r.u()?, r.u()?, r.u()?];
                        if t.iter().any(|&k| k as usize >= nv) {
                            return None;
                        }
                        mesh.tri.push(t);
                    }
                    parts.push(Part { mesh: Arc::new(mesh), m: M3(m), off, color: if c[0] < 0.0 { None } else { Some(c) } });
                }
                objs.push(Obj { id, parts: Arc::new(parts), pos, rot: M3(rot), scale, color, finish, group });
            }
            Some((w, h, px, view, next_id, objs))
        };
        match parse(data) {
            Some((w, h, px, view, next_id, objs)) => {
                self.w = w;
                self.h = h;
                self.px = px;
                self.view = view;
                self.next_id = next_id.max(objs.iter().map(|o| o.id + 1).max().unwrap_or(1));
                self.objs = objs;
                self.undo.clear();
                self.redo.clear();
                self.resized();
                true
            }
            None => false,
        }
    }
}

// --------------------------------------------------------------------- C ABI

unsafe fn d<'a>(c: *mut Doc) -> Option<&'a mut Doc> {
    c.as_mut()
}

#[no_mangle]
pub extern "C" fn op3d_new(w: i32, h: i32) -> *mut Doc {
    Box::into_raw(Box::new(Doc::new(w.max(1) as usize, h.max(1) as usize)))
}

#[no_mangle]
pub unsafe extern "C" fn op3d_free(c: *mut Doc) {
    if !c.is_null() {
        drop(Box::from_raw(c));
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_reset(c: *mut Doc, w: i32, h: i32) {
    if let Some(c) = d(c) {
        *c = Doc::new(w.max(1) as usize, h.max(1) as usize);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_width(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.w as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_height(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.h as i32)
}

/// Compose the display image (straight RGBA) and return a pointer to it.
#[no_mangle]
pub unsafe extern "C" fn op3d_display(c: *mut Doc, ss: i32) -> *const u8 {
    match d(c) {
        Some(c) => {
            c.update_display(ss.max(1) as usize);
            c.disp.as_ptr()
        }
        None => std::ptr::null(),
    }
}

/// Raw canvas pixels (straight RGBA, without background, 3D or effects).
#[no_mangle]
pub unsafe extern "C" fn op3d_pixels(c: *mut Doc) -> *const u8 {
    d(c).map_or(std::ptr::null(), |c| c.px.as_ptr())
}

#[no_mangle]
pub unsafe extern "C" fn op3d_get_view(c: *mut Doc, out: *mut View) {
    if let (Some(c), Some(o)) = (d(c), out.as_mut()) {
        *o = c.view;
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_set_view(c: *mut Doc, v: *const View, label: u32) {
    if let (Some(c), Some(v)) = (d(c), v.as_ref()) {
        if c.view == *v {
            return;
        }
        if label != 0 {
            c.push_undo(label);
        }
        c.view = *v;
        c.scene_changed();
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_checkpoint(c: *mut Doc, label: u32) {
    if let Some(c) = d(c) {
        c.push_undo(label);
    }
}

#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn op3d_stroke_begin(c: *mut Doc, tool: u32, color: u32, size: f32, opacity: f32, finish: u32, x: f32, y: f32) {
    if let Some(c) = d(c) {
        c.stroke_begin(tool, color, size, opacity, finish, x, y);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_stroke_to(c: *mut Doc, x: f32, y: f32) {
    if let Some(c) = d(c) {
        c.stroke_to(x, y);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_stroke_end(c: *mut Doc) {
    if let Some(c) = d(c) {
        c.stroke_end();
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_fill(c: *mut Doc, x: i32, y: i32, color: u32, tol: u32) -> i32 {
    d(c).map_or(0, |c| c.fill(x, y, color, tol) as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_pick(c: *mut Doc, x: i32, y: i32) -> u32 {
    d(c).map_or(0, |c| c.pick(x, y))
}

#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn op3d_blend_bgra(c: *mut Doc, src: *const u8, sw: i32, sh: i32, dx: i32, dy: i32, opacity: f32, finish: u32, label: u32) {
    if let Some(c) = d(c) {
        if src.is_null() || sw <= 0 || sh <= 0 {
            return;
        }
        let s = slice::from_raw_parts(src, sw as usize * sh as usize * 4);
        c.blend_bgra(s, sw as usize, sh as usize, dx, dy, opacity, finish, label);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_load_rgba(c: *mut Doc, src: *const u8, w: i32, h: i32) {
    if let Some(c) = d(c) {
        if src.is_null() || w <= 0 || h <= 0 {
            return;
        }
        c.load_rgba(slice::from_raw_parts(src, w as usize * h as usize * 4), w as usize, h as usize);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_resize(c: *mut Doc, w: i32, h: i32, scale: i32) {
    if let Some(c) = d(c) {
        c.resize(w.max(1) as usize, h.max(1) as usize, scale != 0);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_crop(c: *mut Doc, x: i32, y: i32, w: i32, h: i32) -> i32 {
    d(c).map_or(0, |c| c.crop(x, y, w, h) as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_rotate(c: *mut Doc, cw: i32) {
    if let Some(c) = d(c) {
        c.rotate(cw != 0);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_flip(c: *mut Doc, horizontal: i32) {
    if let Some(c) = d(c) {
        c.flip(horizontal != 0);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_undo(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.undo() as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_redo(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.redo() as i32)
}

/// Serial of the newest undo step (0 when there is none); unlike the count it changes
/// even when the history is full and old steps fall off.
#[no_mangle]
pub unsafe extern "C" fn op3d_history_serial(c: *mut Doc) -> u64 {
    d(c).and_then(|c| c.undo.last()).map_or(0, |s| s.serial)
}

/// Undo the last step without making it redoable (cancelling a lifted selection).
#[no_mangle]
pub unsafe extern "C" fn op3d_undo_discard(c: *mut Doc) -> i32 {
    let Some(c) = d(c) else { return 0 };
    match c.undo.pop() {
        Some(s) => {
            c.restore(s);
            1
        }
        None => 0,
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_undo_count(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.undo.len() as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_redo_count(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.redo.len() as i32)
}

/// Label of an undo (redo = 0) or redo (redo = 1) entry; index 0 is the most recent.
#[no_mangle]
pub unsafe extern "C" fn op3d_history_label(c: *mut Doc, redo: i32, i: i32) -> u32 {
    d(c).map_or(0, |c| {
        let list = if redo != 0 { &c.redo } else { &c.undo };
        list.len().checked_sub(1 + i.max(0) as usize).and_then(|k| list.get(k)).map_or(0, |s| s.label)
    })
}

// --- 3D objects

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_begin(c: *mut Doc) {
    if let Some(c) = d(c) {
        c.builder.clear();
    }
}

fn part_transform(t: *const f32) -> Option<(V3, M3)> {
    // [ox, oy, oz, sx, sy, sz, rx, ry, rz]
    let t = unsafe { slice::from_raw_parts(t.as_ref()?, 9) };
    Some((v3(t[0], t[1], t[2]), M3::euler(t[6], t[7], t[8]).mul(&M3::scale(v3(t[3], t[4], t[5])))))
}

fn part_color(color: u32) -> Option<[f32; 3]> {
    let (c, a) = unpack(color);
    if a > 0.0 { Some(c) } else { None }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_part(c: *mut Doc, prim: u32, transform: *const f32, color: u32) {
    if let (Some(c), Some((off, m))) = (d(c), part_transform(transform)) {
        c.builder.push(Part { mesh: Arc::new(Mesh::primitive(prim)), m, off, color: part_color(color) });
    }
}

/// Add a part built from a coverage mask: mode 0 = extrude (sharp), 1 = inflate (soft).
#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn op3d_obj_part_mask(c: *mut Doc, mask: *const u8, mw: i32, mh: i32, mode: u32, step: i32, transform: *const f32, color: u32) {
    if let (Some(c), Some((off, m))) = (d(c), part_transform(transform)) {
        if mask.is_null() || mw <= 0 || mh <= 0 {
            return;
        }
        let a = slice::from_raw_parts(mask, mw as usize * mh as usize);
        let mk = mesh::Mask { w: mw as usize, h: mh as usize, a };
        let mesh = if mode == 1 { mesh::inflate(&mk, step.max(1) as usize) } else { mesh::extrude(&mk, step.max(1) as usize) };
        if !mesh.tri.is_empty() {
            c.builder.push(Part { mesh: Arc::new(mesh), m, off, color: part_color(color) });
        }
    }
}

/// Create an object from the parts added since op3d_obj_begin. `t` = [px,py,pz, sx,sy,sz, rx,ry,rz].
#[no_mangle]
pub unsafe extern "C" fn op3d_obj_commit(c: *mut Doc, t: *const f32, color: u32, finish: u32, label: u32) -> u32 {
    let (Some(c), Some(t)) = (d(c), t.as_ref()) else { return 0 };
    if c.builder.is_empty() {
        return 0;
    }
    let t = slice::from_raw_parts(t, 9);
    if label != 0 {
        c.push_undo(label);
    }
    let id = c.next_id;
    c.next_id += 1;
    let (col, _) = unpack(color);
    c.objs.push(Obj {
        id,
        parts: Arc::new(std::mem::take(&mut c.builder)),
        pos: v3(t[0], t[1], t[2]),
        rot: M3::euler(t[6], t[7], t[8]),
        scale: v3(t[3], t[4], t[5]),
        color: col,
        finish,
        group: 0,
    });
    c.scene_changed();
    id
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_set(c: *mut Doc, id: u32, px: f32, py: f32, pz: f32, sx: f32, sy: f32, sz: f32) {
    if let Some(c) = d(c) {
        if let Some(o) = c.obj_mut(id) {
            o.pos = v3(px, py, pz);
            o.scale = v3(sx, sy, sz);
            c.scene_changed();
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_rotate(c: *mut Doc, id: u32, axis: u32, deg: f32) {
    if let Some(c) = d(c) {
        if let Some(o) = c.obj_mut(id) {
            o.rot = M3::rot_axis(axis, deg).mul(&o.rot);
            c.scene_changed();
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_material(c: *mut Doc, id: u32, color: u32, finish: u32) {
    if let Some(c) = d(c) {
        let (col, _) = unpack(color);
        if let Some(o) = c.obj_mut(id) {
            o.color = col;
            o.finish = finish;
            c.scene_changed();
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_delete(c: *mut Doc, id: u32) -> i32 {
    let Some(c) = d(c) else { return 0 };
    if !c.objs.iter().any(|o| o.id == id) {
        return 0;
    }
    c.push_undo(9);
    c.objs.retain(|o| o.id != id);
    c.scene_changed();
    1
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_duplicate(c: *mut Doc, id: u32) -> u32 {
    let Some(c) = d(c) else { return 0 };
    let Some(src) = c.objs.iter().find(|o| o.id == id).cloned() else { return 0 };
    c.push_undo(7);
    let nid = c.next_id;
    c.next_id += 1;
    c.objs.push(Obj { id: nid, pos: src.pos + v3(30.0, 30.0, 0.0), group: 0, ..src });
    c.scene_changed();
    nid
}

/// out = [pos xyz, scale xyz, screen x0 y0 x1 y1, colour rgb, finish]
#[no_mangle]
pub unsafe extern "C" fn op3d_obj_info(c: *mut Doc, id: u32, out: *mut f32) -> i32 {
    let (Some(c), Some(out)) = (d(c), out.as_mut()) else { return 0 };
    let Some(o) = c.objs.iter().find(|o| o.id == id) else { return 0 };
    let out = slice::from_raw_parts_mut(out, 14);
    let cam = Camera::canvas(c.w as f32, c.h as f32, 1.0);
    let (mut x0, mut y0, mut x1, mut y1) = (f32::MAX, f32::MAX, f32::MIN, f32::MIN);
    for p in o.corners() {
        let (x, y, _) = cam.project(p);
        x0 = x0.min(x);
        y0 = y0.min(y);
        x1 = x1.max(x);
        y1 = y1.max(y);
    }
    let vals = [o.pos.x, o.pos.y, o.pos.z, o.scale.x, o.scale.y, o.scale.z, x0, y0, x1, y1, o.color[0], o.color[1], o.color[2], o.finish as f32];
    out.copy_from_slice(&vals);
    1
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_pick(c: *mut Doc, x: i32, y: i32) -> u32 {
    let Some(c) = d(c) else { return 0 };
    if x < 0 || y < 0 || x as usize >= c.w || y as usize >= c.h || c.objs.is_empty() {
        return 0;
    }
    let ss = c.layer_ss.max(1);
    c.ensure_layer(ss);
    c.layer_id.get(y as usize * c.w + x as usize).copied().unwrap_or(0)
}

/// Rotate around a world axis through (cx, cy, cz), moving the object's position too
/// (used to turn several selected objects as one).
#[no_mangle]
pub unsafe extern "C" fn op3d_obj_rotate_about(c: *mut Doc, id: u32, axis: u32, deg: f32, cx: f32, cy: f32, cz: f32) {
    if let Some(c) = d(c) {
        if let Some(o) = c.obj_mut(id) {
            let r = M3::rot_axis(axis, deg);
            let centre = v3(cx, cy, cz);
            o.rot = r.mul(&o.rot);
            o.pos = r.apply(o.pos - centre) + centre;
            c.scene_changed();
        }
    }
}

/// Copy of an object offset by (dx, dy), without a history step (the caller checkpoints).
#[no_mangle]
pub unsafe extern "C" fn op3d_obj_clone(c: *mut Doc, id: u32, dx: f32, dy: f32, group: u32) -> u32 {
    let Some(c) = d(c) else { return 0 };
    let Some(src) = c.objs.iter().find(|o| o.id == id).cloned() else { return 0 };
    let nid = c.next_id;
    c.next_id += 1;
    c.objs.push(Obj { id: nid, pos: src.pos + v3(dx, dy, 0.0), group, ..src });
    c.scene_changed();
    nid
}

/// Remove an object without a history step (the caller checkpoints).
#[no_mangle]
pub unsafe extern "C" fn op3d_obj_remove(c: *mut Doc, id: u32) -> i32 {
    let Some(c) = d(c) else { return 0 };
    let n = c.objs.len();
    c.objs.retain(|o| o.id != id);
    c.scene_changed();
    (c.objs.len() != n) as i32
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_set_group(c: *mut Doc, id: u32, group: u32) {
    if let Some(c) = d(c) {
        if let Some(o) = c.obj_mut(id) {
            o.group = group;
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_group(c: *mut Doc, id: u32) -> u32 {
    d(c).and_then(|c| c.objs.iter().find(|o| o.id == id)).map_or(0, |o| o.group)
}

/// Ids of all objects in back-to-front creation order; returns the count written (at most `max`).
#[no_mangle]
pub unsafe extern "C" fn op3d_obj_ids(c: *mut Doc, out: *mut u32, max: i32) -> i32 {
    let Some(c) = d(c) else { return 0 };
    if out.is_null() || max <= 0 {
        return c.objs.len() as i32;
    }
    let out = slice::from_raw_parts_mut(out, max as usize);
    for (slot, o) in out.iter_mut().zip(&c.objs) {
        *slot = o.id;
    }
    c.objs.len().min(max as usize) as i32
}

/// Make a rectangle of the canvas transparent (lifting a 2D selection); records history when `label` is set.
#[no_mangle]
pub unsafe extern "C" fn op3d_clear_rect(c: *mut Doc, x: i32, y: i32, w: i32, h: i32, label: u32) {
    let Some(c) = d(c) else { return };
    let Some(r) = c.clip(x as f32, y as f32, (x + w - 1) as f32, (y + h - 1) as f32) else { return };
    if w <= 0 || h <= 0 {
        return;
    }
    if label != 0 {
        c.push_undo(label);
    }
    for yy in r.y0..=r.y1 {
        let row = (yy * c.w + r.x0) * 4;
        c.px[row..row + (r.x1 - r.x0 + 1) * 4].fill(0);
    }
    c.mark(r);
}

#[no_mangle]
pub unsafe extern "C" fn op3d_clip_clear(c: *mut Doc) {
    if let Some(c) = d(c) {
        c.clip.clear();
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_clip_add(c: *mut Doc, id: u32) {
    if let Some(c) = d(c) {
        if let Some(o) = c.objs.iter().find(|o| o.id == id).cloned() {
            c.clip.push(o);
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_clip_count(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.clip.len() as i32)
}

/// Add copies of the clipboard objects offset by (dx, dy) with fresh ids and groups, without a
/// history step. The new objects are the last `count` ids of `op3d_obj_ids`.
#[no_mangle]
pub unsafe extern "C" fn op3d_clip_paste(c: *mut Doc, dx: f32, dy: f32) -> i32 {
    let Some(c) = d(c) else { return 0 };
    let mut next_group = c.objs.iter().map(|o| o.group).max().unwrap_or(0) + 1;
    let mut remap: Vec<(u32, u32)> = Vec::new();
    for o in c.clip.clone() {
        let group = if o.group == 0 {
            0
        } else if let Some(&(_, g)) = remap.iter().find(|(old, _)| *old == o.group) {
            g
        } else {
            remap.push((o.group, next_group));
            next_group += 1;
            next_group - 1
        };
        let id = c.next_id;
        c.next_id += 1;
        c.objs.push(Obj { id, pos: o.pos + v3(dx, dy, 0.0), group, ..o });
    }
    c.scene_changed();
    c.clip.len() as i32
}

#[no_mangle]
pub unsafe extern "C" fn op3d_set_perspective(c: *mut Doc, on: i32) {
    if let Some(c) = d(c) {
        c.perspective = on != 0;
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_obj_count(c: *mut Doc) -> i32 {
    d(c).map_or(0, |c| c.objs.len() as i32)
}

#[no_mangle]
#[allow(clippy::too_many_arguments)]
pub unsafe extern "C" fn op3d_render_3d(c: *mut Doc, out: *mut u8, ow: i32, oh: i32, yaw: f32, pitch: f32, zoom: f32, ss: i32) {
    if let Some(c) = d(c) {
        if out.is_null() || ow <= 0 || oh <= 0 {
            return;
        }
        let o = slice::from_raw_parts_mut(out, ow as usize * oh as usize * 4);
        c.render_3d(o, ow as usize, oh as usize, yaw, pitch, zoom, ss.max(1) as usize);
    }
}

/// World-space triangle soup for 3D export: 9 floats per vertex (pos, normal, rgb).
#[no_mangle]
pub unsafe extern "C" fn op3d_export_tris(c: *mut Doc, count: *mut i32) -> *const f32 {
    let Some(c) = d(c) else { return std::ptr::null() };
    let n = c.export_tris();
    if let Some(cnt) = count.as_mut() {
        *cnt = n as i32;
    }
    c.out_f32.as_ptr()
}

#[no_mangle]
pub unsafe extern "C" fn op3d_save(c: *mut Doc, len: *mut i32) -> *const u8 {
    let Some(c) = d(c) else { return std::ptr::null() };
    let n = c.save();
    if let Some(l) = len.as_mut() {
        *l = n as i32;
    }
    c.out_u8.as_ptr()
}

#[no_mangle]
pub unsafe extern "C" fn op3d_load(c: *mut Doc, data: *const u8, len: i32) -> i32 {
    let Some(c) = d(c) else { return 0 };
    if data.is_null() || len <= 0 {
        return 0;
    }
    c.load(slice::from_raw_parts(data, len as usize)) as i32
}

#[cfg(test)]
mod tests {
    use super::*;

    fn px(c: &Doc, x: usize, y: usize) -> [u8; 4] {
        let i = (y * c.w + x) * 4;
        [c.px[i], c.px[i + 1], c.px[i + 2], c.px[i + 3]]
    }

    fn disp(c: &mut Doc, x: usize, y: usize) -> [u8; 4] {
        c.update_display(1);
        let i = (y * c.w + x) * 4;
        [c.disp[i], c.disp[i + 1], c.disp[i + 2], c.disp[i + 3]]
    }

    #[test]
    fn stroke_paints_erases_and_undo_restores() {
        let mut c = Doc::new(64, 64);
        c.stroke_begin(TOOL_MARKER, 0xFF0000FF, 8.0, 1.0, 0, 10.0, 32.0);
        c.stroke_to(50.0, 32.0);
        c.stroke_end();
        assert_eq!(px(&c, 30, 32), [255, 0, 0, 255]);
        c.stroke_begin(TOOL_ERASER, 0, 8.0, 1.0, 0, 30.0, 32.0);
        c.stroke_end();
        assert_eq!(px(&c, 30, 32)[3], 0);
        // erased pixels show the canvas background
        assert_eq!(disp(&mut c, 30, 32), [255, 255, 255, 255]);
        assert!(c.undo());
        assert_eq!(px(&c, 30, 32), [255, 0, 0, 255]);
        assert!(c.undo());
        assert_eq!(px(&c, 30, 32)[3], 0);
        assert!(c.redo());
        assert_eq!(px(&c, 30, 32), [255, 0, 0, 255]);
    }

    #[test]
    fn opacity_does_not_accumulate_within_stroke() {
        let mut c = Doc::new(64, 64);
        c.stroke_begin(TOOL_MARKER, 0x000000FF, 10.0, 0.5, 0, 20.0, 32.0);
        for _ in 0..5 {
            c.stroke_to(40.0, 32.0);
            c.stroke_to(20.0, 32.0);
        }
        c.stroke_end();
        let a = px(&c, 30, 32)[3];
        assert!((126..=129).contains(&a), "got {a}");
        // composed over the white background it reads as mid grey
        let v = disp(&mut c, 30, 32)[0];
        assert!((125..=130).contains(&v), "got {v}");
    }

    #[test]
    fn hidden_background_is_transparent_and_custom_bg_shows() {
        let mut c = Doc::new(10, 10);
        let mut v = c.view;
        v.show_bg = 0;
        unsafe { op3d_set_view(&mut c, &v, 0) };
        assert_eq!(disp(&mut c, 5, 5)[3], 0);
        v.show_bg = 1;
        v.bg = 0x336699FF;
        unsafe { op3d_set_view(&mut c, &v, 0) };
        assert_eq!(disp(&mut c, 5, 5), [0x33, 0x66, 0x99, 255]);
    }

    #[test]
    fn glossy_stroke_has_highlight_and_shadow_side() {
        let mut c = Doc::new(80, 80);
        c.stroke_begin(TOOL_MARKER, 0x2060C0FF, 30.0, 1.0, scene::FINISH_GLOSS, 10.0, 40.0);
        c.stroke_to(70.0, 40.0);
        c.stroke_end();
        let top = px(&c, 40, 31);
        let bottom = px(&c, 40, 49);
        let s = |p: [u8; 4]| p[0] as u32 + p[1] as u32 + p[2] as u32;
        assert!(s(top) > s(bottom), "{top:?} vs {bottom:?}");
    }

    #[test]
    fn flood_fill_on_transparent_canvas() {
        let mut c = Doc::new(50, 50);
        c.stroke_begin(TOOL_PIXEL, 0x000000FF, 2.0, 1.0, 0, 10.0, 10.0);
        for (x, y) in [(40.0, 10.0), (40.0, 40.0), (10.0, 40.0), (10.0, 10.0)] {
            c.stroke_to(x, y);
        }
        c.stroke_end();
        assert!(c.fill(25, 25, 0xFF0000FF, 10));
        assert_eq!(px(&c, 25, 25), [255, 0, 0, 255]);
        assert_eq!(px(&c, 2, 2)[3], 0);
    }

    fn add_cube(c: &mut Doc) -> u32 {
        unsafe {
            op3d_obj_begin(c);
            let part = [0.0f32, 0.0, 0.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0];
            op3d_obj_part(c, mesh::PRIM_CUBE, part.as_ptr(), 0);
            let t = [50.0f32, 50.0, -30.0, 40.0, 40.0, 40.0, 20.0, 30.0, 0.0];
            op3d_obj_commit(c, t.as_ptr(), 0x3366CCFF, scene::FINISH_MATTE, 7)
        }
    }

    #[test]
    fn objects_render_pick_transform_undo_and_survive_save_load() {
        let mut c = Doc::new(100, 100);
        let id = add_cube(&mut c);
        assert!(id > 0);
        assert_eq!(unsafe { op3d_obj_pick(&mut c, 50, 50) }, id);
        assert_eq!(unsafe { op3d_obj_pick(&mut c, 2, 2) }, 0);
        let p = disp(&mut c, 50, 50);
        assert!(p[2] > p[0], "cube should look blue: {p:?}");
        unsafe {
            op3d_checkpoint(&mut c, 8);
            op3d_obj_set(&mut c, id, 20.0, 20.0, -30.0, 20.0, 20.0, 20.0);
        }
        assert_eq!(unsafe { op3d_obj_pick(&mut c, 50, 50) }, 0);
        let mut len = 0;
        let ptr = unsafe { op3d_save(&mut c, &mut len) };
        let blob = unsafe { slice::from_raw_parts(ptr, len as usize) }.to_vec();
        let mut c2 = Doc::new(1, 1);
        assert!(c2.load(&blob));
        assert_eq!((c2.w, c2.h, c2.objs.len()), (100, 100, 1));
        assert_eq!(unsafe { op3d_obj_pick(&mut c2, 20, 20) }, id);
        assert!(c.undo());
        assert_eq!(unsafe { op3d_obj_pick(&mut c, 50, 50) }, id);
        assert!(c.export_tris() >= 36);
    }

    #[test]
    fn canvas_ops_move_objects() {
        let mut c = Doc::new(100, 80);
        let id = add_cube(&mut c);
        c.crop(10, 10, 80, 60);
        assert_eq!(c.objs[0].pos.x, 40.0);
        // expanding past the edge keeps old pixels in place and adds transparency
        let mut e = Doc::new(10, 10);
        e.fill(0, 0, 0xFF0000FF, 0);
        assert!(e.crop(-5, 0, 20, 10));
        assert_eq!((e.w, px(&e, 2, 2)[3], px(&e, 7, 2)), (20, 0, [255, 0, 0, 255]));
        c.rotate(true);
        assert_eq!((c.w, c.h), (60, 80));
        assert_eq!(unsafe { op3d_obj_pick(&mut c, 20, 40) }, id);
        c.resize(120, 160, true);
        assert_eq!(unsafe { op3d_obj_pick(&mut c, 40, 80) }, id);
        for _ in 0..3 {
            c.undo();
        }
        assert_eq!((c.w, c.h), (100, 80));
    }

    #[test]
    fn mask_parts_and_3d_view() {
        let mut c = Doc::new(120, 90);
        let n = 40usize;
        let mask: Vec<u8> = (0..n * n).map(|i| if (i % n) > 8 && (i % n) < 32 && (i / n) > 8 && (i / n) < 32 { 255 } else { 0 }).collect();
        let part = [0.0f32, 0.0, 0.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0];
        unsafe {
            for mode in 0..2 {
                op3d_obj_begin(&mut c);
                op3d_obj_part_mask(&mut c, mask.as_ptr(), n as i32, n as i32, mode, 1, part.as_ptr(), 0);
                let t = [60.0f32, 45.0, -10.0, 60.0, 60.0, 20.0, 0.0, 0.0, 0.0];
                assert!(op3d_obj_commit(&mut c, t.as_ptr(), 0xCC3333FF, scene::FINISH_GLOSS, 7) > 0);
            }
        }
        assert!(unsafe { op3d_obj_pick(&mut c, 60, 45) } > 0);
        let mut out = vec![0u8; 64 * 48 * 4];
        c.render_3d(&mut out, 64, 48, 30.0, 20.0, 1.0, 1);
        assert!(out.chunks(4).any(|p| p[0] > p[2] + 40), "red object visible in 3D view");
    }

    #[test]
    fn history_labels() {
        let mut c = Doc::new(20, 20);
        c.fill(1, 1, 0xFF0000FF, 0);
        add_cube(&mut c);
        unsafe {
            assert_eq!(op3d_history_label(&mut c, 0, 0), 7);
            assert_eq!(op3d_history_label(&mut c, 0, 1), 3);
            c.undo();
            assert_eq!(op3d_history_label(&mut c, 1, 0), 7);
        }
    }

    #[test]
    fn undoing_several_size_changes_before_display_does_not_overrun() {
        let mut c = Doc::new(40, 30);
        c.crop(0, 0, 60, 30);
        unsafe { op3d_display(&mut c, 1) };
        c.fill(50, 5, 0xFF0000FF, 0);
        assert!(c.undo() && c.undo());
        assert_eq!(c.w, 40);
        unsafe { op3d_display(&mut c, 1) };
    }

    #[test]
    fn groups_clear_rect_and_project_v2() {
        let mut c = Doc::new(40, 40);
        c.fill(0, 0, 0x00FF00FF, 0);
        let a = add_cube(&mut c);
        unsafe {
            let b = op3d_obj_clone(&mut c, a, 5.0, 0.0, 0);
            op3d_obj_set_group(&mut c, a, 3);
            op3d_obj_set_group(&mut c, b, 3);
            let mut ids = [0u32; 4];
            assert_eq!(op3d_obj_ids(&mut c, ids.as_mut_ptr(), 4), 2);
            assert_eq!(ids[..2], [a, b]);
            op3d_obj_rotate_about(&mut c, b, 2, 180.0, 0.0, 0.0, 0.0);
            assert!(c.objs[1].pos.x < 0.0);
            op3d_clear_rect(&mut c, 2, 2, 4, 4, 18);
        }
        assert_eq!((px(&c, 3, 3)[3], px(&c, 10, 10)[3]), (0, 255));
        c.save();
        let data = c.out_u8.clone();
        let mut e = Doc::new(1, 1);
        assert!(e.load(&data));
        assert_eq!((e.objs.len(), e.objs[0].group, e.objs[1].group), (2, 3, 3));
        let lift = unsafe { op3d_history_serial(&mut c) };
        assert!(c.undo() && c.redo());
        assert_eq!(unsafe { op3d_history_serial(&mut c) }, lift, "a step keeps its serial through undo/redo");
        let redo = c.redo.len();
        unsafe { assert_eq!(op3d_undo_discard(&mut c), 1) };
        assert_eq!((px(&c, 3, 3)[3], c.redo.len()), (255, redo));
        unsafe {
            op3d_clip_add(&mut c, a);
            op3d_clip_add(&mut c, c.objs[1].id);
            assert_eq!(op3d_obj_remove(&mut c, a), 1);
            assert_eq!(c.objs.len(), 1);
            assert_eq!(op3d_clip_paste(&mut c, 10.0, 0.0), 2);
        }
        assert_eq!(c.objs.len(), 3);
        assert_eq!(c.objs[1].group, c.objs[2].group);
        assert!(c.objs[1].group != 0 && c.objs[1].group != c.objs[0].group, "pasted group gets a fresh id");
    }
}
