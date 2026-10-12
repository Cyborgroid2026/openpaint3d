//! OpenPaint 3D raster engine.
//!
//! The canvas is always opaque and stored as straight RGBA8. All drawing
//! operations are exposed through a small C ABI consumed by the C# front end.

use std::slice;

const MAX_UNDO: usize = 40;

// Brush tools
pub const TOOL_MARKER: u32 = 0;
pub const TOOL_CALLIGRAPHY: u32 = 1;
pub const TOOL_OIL: u32 = 2;
pub const TOOL_WATERCOLOR: u32 = 3;
pub const TOOL_PIXEL: u32 = 4;
pub const TOOL_PENCIL: u32 = 5;
pub const TOOL_ERASER: u32 = 6;
pub const TOOL_CRAYON: u32 = 7;
pub const TOOL_SPRAY: u32 = 8;

struct Snap {
    w: usize,
    h: usize,
    px: Vec<u8>,
}

struct Stroke {
    tool: u32,
    color: [f32; 3],
    size: f32,
    opacity: f32,
    last: (f32, f32),
    carry: f32,
}

pub struct Canvas {
    w: usize,
    h: usize,
    px: Vec<u8>,
    base: Vec<u8>,
    mask: Vec<f32>,
    stroke: Option<Stroke>,
    undo: Vec<Snap>,
    redo: Vec<Snap>,
    rng: u64,
}

#[derive(Clone, Copy)]
struct Bbox {
    x0: usize,
    y0: usize,
    x1: usize,
    y1: usize,
}

fn clamp01(v: f32) -> f32 {
    v.max(0.0).min(1.0)
}

fn hash(x: i32, y: i32) -> f32 {
    let mut h = (x as u32).wrapping_mul(374_761_393) ^ (y as u32).wrapping_mul(668_265_263);
    h = (h ^ (h >> 13)).wrapping_mul(1_274_126_177);
    h ^= h >> 16;
    (h & 0xffff) as f32 / 65535.0
}

/// 0xRRGGBBAA -> [r, g, b] in 0..255 and alpha in 0..1
fn unpack(c: u32) -> ([f32; 3], f32) {
    (
        [((c >> 24) & 255) as f32, ((c >> 16) & 255) as f32, ((c >> 8) & 255) as f32],
        (c & 255) as f32 / 255.0,
    )
}

fn shade(c: [f32; 3], k: f32, spec: f32) -> [f32; 3] {
    [
        (c[0] * k + 255.0 * spec).min(255.0),
        (c[1] * k + 255.0 * spec).min(255.0),
        (c[2] * k + 255.0 * spec).min(255.0),
    ]
}

/// Signed distance to a closed polygon (negative inside).
fn polygon_sdf(pts: &[(f32, f32)], p: (f32, f32)) -> f32 {
    let n = pts.len();
    let mut d = (p.0 - pts[0].0).powi(2) + (p.1 - pts[0].1).powi(2);
    let mut s = 1.0f32;
    let mut j = n - 1;
    for i in 0..n {
        let (vi, vj) = (pts[i], pts[j]);
        let e = (vj.0 - vi.0, vj.1 - vi.1);
        let w = (p.0 - vi.0, p.1 - vi.1);
        let ee = (e.0 * e.0 + e.1 * e.1).max(1e-9);
        let t = clamp01((w.0 * e.0 + w.1 * e.1) / ee);
        let b = (w.0 - e.0 * t, w.1 - e.1 * t);
        d = d.min(b.0 * b.0 + b.1 * b.1);
        let c1 = p.1 >= vi.1;
        let c2 = p.1 < vj.1;
        let c3 = e.0 * w.1 > e.1 * w.0;
        if (c1 && c2 && c3) || (!c1 && !c2 && !c3) {
            s = -s;
        }
        j = i;
    }
    s * d.sqrt()
}

fn segment_dist(p: (f32, f32), a: (f32, f32), b: (f32, f32)) -> f32 {
    let ab = (b.0 - a.0, b.1 - a.1);
    let ap = (p.0 - a.0, p.1 - a.1);
    let ll = (ab.0 * ab.0 + ab.1 * ab.1).max(1e-9);
    let t = clamp01((ap.0 * ab.0 + ap.1 * ab.1) / ll);
    ((ap.0 - ab.0 * t).powi(2) + (ap.1 - ab.1 * t).powi(2)).sqrt()
}

fn regular_polygon(n: usize, cx: f32, cy: f32, rx: f32, ry: f32, inner: f32) -> Vec<(f32, f32)> {
    let total = if inner > 0.0 { n * 2 } else { n };
    (0..total)
        .map(|i| {
            let a = -std::f32::consts::FRAC_PI_2 + i as f32 * std::f32::consts::TAU / total as f32;
            let k = if inner > 0.0 && i % 2 == 1 { inner } else { 1.0 };
            (cx + a.cos() * rx * k, cy + a.sin() * ry * k)
        })
        .collect()
}

/// Outline points for a 2D shape kind inside the box (x0,y0)-(x1,y1).
fn shape_points(kind: u32, x0: f32, y0: f32, x1: f32, y1: f32) -> Vec<(f32, f32)> {
    let (cx, cy) = ((x0 + x1) / 2.0, (y0 + y1) / 2.0);
    let (rx, ry) = ((x1 - x0) / 2.0, (y1 - y0) / 2.0);
    let (w, h) = (x1 - x0, y1 - y0);
    match kind {
        1 => vec![(x0, y0), (x1, y0), (x1, y1), (x0, y1)],
        2 => regular_polygon(128, cx, cy, rx, ry, 0.0),
        3 => vec![(cx, y0), (x1, y1), (x0, y1)],
        4 => vec![(x0, y0), (x1, y1), (x0, y1)],
        5 => regular_polygon(5, cx, cy, rx, ry, 0.0),
        6 => regular_polygon(6, cx, cy, rx, ry, 0.0),
        7 => regular_polygon(5, cx, cy, rx, ry, 0.45),
        8 => (0..128)
            .map(|i| {
                let t = i as f32 * std::f32::consts::TAU / 128.0;
                let hx = 16.0 * t.sin().powi(3);
                let hy = 13.0 * t.cos() - 5.0 * (2.0 * t).cos() - 2.0 * (3.0 * t).cos() - (4.0 * t).cos();
                (cx + hx / 16.0 * rx, y0 + (12.0 - hy) / 29.0 * h)
            })
            .collect(),
        9 => vec![
            (x0, y0 + h * 0.3),
            (x0 + w * 0.6, y0 + h * 0.3),
            (x0 + w * 0.6, y0),
            (x1, cy),
            (x0 + w * 0.6, y1),
            (x0 + w * 0.6, y0 + h * 0.7),
            (x0, y0 + h * 0.7),
        ],
        _ => vec![(x0, y0), (x1, y0), (x1, y1), (x0, y1)],
    }
}

impl Canvas {
    fn new(w: usize, h: usize) -> Self {
        let (w, h) = (w.max(1), h.max(1));
        Canvas {
            w,
            h,
            px: vec![255; w * h * 4],
            base: Vec::new(),
            mask: Vec::new(),
            stroke: None,
            undo: Vec::new(),
            redo: Vec::new(),
            rng: 0x9E37_79B9_7F4A_7C15,
        }
    }

    fn rand(&mut self) -> f32 {
        self.rng ^= self.rng << 13;
        self.rng ^= self.rng >> 7;
        self.rng ^= self.rng << 17;
        (self.rng >> 40) as f32 / (1u64 << 24) as f32
    }

    fn push_undo(&mut self) {
        self.undo.push(Snap { w: self.w, h: self.h, px: self.px.clone() });
        if self.undo.len() > MAX_UNDO {
            self.undo.remove(0);
        }
        self.redo.clear();
    }

    fn replace(&mut self, w: usize, h: usize, px: Vec<u8>) {
        self.w = w;
        self.h = h;
        self.px = px;
    }

    fn undo(&mut self) -> bool {
        match self.undo.pop() {
            Some(s) => {
                let cur = Snap { w: self.w, h: self.h, px: std::mem::take(&mut self.px) };
                self.redo.push(cur);
                self.replace(s.w, s.h, s.px);
                true
            }
            None => false,
        }
    }

    fn redo(&mut self) -> bool {
        match self.redo.pop() {
            Some(s) => {
                let cur = Snap { w: self.w, h: self.h, px: std::mem::take(&mut self.px) };
                self.undo.push(cur);
                self.replace(s.w, s.h, s.px);
                true
            }
            None => false,
        }
    }

    #[inline]
    fn blend(&mut self, x: usize, y: usize, col: [f32; 3], a: f32) {
        if a <= 0.0 {
            return;
        }
        let a = a.min(1.0);
        let i = (y * self.w + x) * 4;
        for k in 0..3 {
            self.px[i + k] = (self.px[i + k] as f32 * (1.0 - a) + col[k] * a + 0.5) as u8;
        }
    }

    fn bbox(&self, x0: f32, y0: f32, x1: f32, y1: f32) -> Option<Bbox> {
        let (ax, bx) = (x0.min(x1).floor().max(0.0), x0.max(x1).ceil().min(self.w as f32 - 1.0));
        let (ay, by) = (y0.min(y1).floor().max(0.0), y0.max(y1).ceil().min(self.h as f32 - 1.0));
        if ax > bx || ay > by {
            return None;
        }
        Some(Bbox { x0: ax as usize, y0: ay as usize, x1: bx as usize, y1: by as usize })
    }

    // ---------------------------------------------------------------- brushes

    fn stroke_begin(&mut self, tool: u32, color: u32, size: f32, opacity: f32, x: f32, y: f32) {
        self.push_undo();
        let (mut col, _) = unpack(color);
        if tool == TOOL_ERASER {
            col = [255.0; 3];
        }
        let opacity = if tool == TOOL_ERASER { 1.0 } else { clamp01(opacity) };
        self.base.clear();
        self.base.extend_from_slice(&self.px);
        self.mask.clear();
        self.mask.resize(self.w * self.h, 0.0);
        self.stroke = Some(Stroke { tool, color: col, size: size.max(1.0), opacity, last: (x, y), carry: 0.0 });
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
            _ => (size * 0.15).max(0.75),
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
    }

    fn stamp(&mut self, cx: f32, cy: f32) {
        let (tool, size, color, opacity) = match &self.stroke {
            Some(s) => (s.tool, s.size, s.color, s.opacity),
            None => return,
        };
        let r = size / 2.0;
        let bb = match self.bbox(cx - r - 1.0, cy - r - 1.0, cx + r + 1.0, cy + r + 1.0) {
            Some(b) => b,
            None => return,
        };
        if tool == TOOL_SPRAY {
            let n = ((r * r * 0.12) as usize).max(4);
            for _ in 0..n {
                let a = self.rand() * std::f32::consts::TAU;
                let d = self.rand().sqrt() * r;
                let (px, py) = (cx + a.cos() * d, cy + a.sin() * d);
                if px >= 0.0 && py >= 0.0 && (px as usize) < self.w && (py as usize) < self.h {
                    self.mask[py as usize * self.w + px as usize] = 1.0;
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
                            if dx.abs() <= rr && dy.abs() <= rr {
                                1.0
                            } else {
                                0.0
                            }
                        }
                        TOOL_PENCIL => edge * (0.45 + 0.55 * hash(x as i32, y as i32)),
                        TOOL_CRAYON => {
                            if hash(x as i32, y as i32 / 2) > 0.35 {
                                edge
                            } else {
                                edge * 0.15
                            }
                        }
                        _ => edge,
                    };
                    let i = y * self.w + x;
                    if cov > self.mask[i] {
                        self.mask[i] = cov;
                    }
                }
            }
        }
        // composite the touched area from the pre-stroke snapshot
        for y in bb.y0..=bb.y1 {
            for x in bb.x0..=bb.x1 {
                let m = self.mask[y * self.w + x] * opacity;
                if m <= 0.0 {
                    continue;
                }
                let i = (y * self.w + x) * 4;
                for k in 0..3 {
                    self.px[i + k] = (self.base[i + k] as f32 * (1.0 - m) + color[k] * m + 0.5) as u8;
                }
            }
        }
    }

    // --------------------------------------------------------------- 2D shapes

    fn shape(&mut self, kind: u32, x0: f32, y0: f32, x1: f32, y1: f32, stroke: u32, thick: f32, fill: u32) {
        let (sc, sa) = unpack(stroke);
        let (fc, fa) = unpack(fill);
        let pad = thick + 2.0;
        let bb = match self.bbox(x0 - pad, y0 - pad, x1 + pad, y1 + pad) {
            Some(b) => b,
            None => return,
        };
        self.push_undo();
        let half = thick / 2.0;
        if kind == 0 {
            for y in bb.y0..=bb.y1 {
                for x in bb.x0..=bb.x1 {
                    let d = segment_dist((x as f32 + 0.5, y as f32 + 0.5), (x0, y0), (x1, y1));
                    self.blend(x, y, sc, clamp01(half.max(0.5) + 0.5 - d) * sa);
                }
            }
            return;
        }
        let (nx0, nx1) = (x0.min(x1), x0.max(x1));
        let (ny0, ny1) = (y0.min(y1), y0.max(y1));
        let pts = shape_points(kind, nx0, ny0, nx1, ny1);
        for y in bb.y0..=bb.y1 {
            for x in bb.x0..=bb.x1 {
                let sd = polygon_sdf(&pts, (x as f32 + 0.5, y as f32 + 0.5));
                if fa > 0.0 {
                    self.blend(x, y, fc, clamp01(0.5 - sd) * fa);
                }
                if sa > 0.0 && thick > 0.0 {
                    self.blend(x, y, sc, clamp01(half + 0.5 - sd.abs()) * sa);
                }
            }
        }
    }

    // ------------------------------------------------------- shaded 3D models

    fn model(&mut self, kind: u32, x0: f32, y0: f32, x1: f32, y1: f32, color: u32) {
        let (c, _) = unpack(color);
        let (x0, x1) = (x0.min(x1), x0.max(x1));
        let (y0, y1) = (y0.min(y1), y0.max(y1));
        let bb = match self.bbox(x0 - 1.0, y0 - 1.0, x1 + 1.0, y1 + 1.0) {
            Some(b) => b,
            None => return,
        };
        self.push_undo();
        let (cx, cy) = ((x0 + x1) / 2.0, (y0 + y1) / 2.0);
        let (rx, ry) = (((x1 - x0) / 2.0).max(1.0), ((y1 - y0) / 2.0).max(1.0));
        let (w, h) = (x1 - x0, y1 - y0);
        let light = {
            let (lx, ly, lz) = (-0.45f32, -0.55f32, 0.7f32);
            let l = (lx * lx + ly * ly + lz * lz).sqrt();
            (lx / l, ly / l, lz / l)
        };
        let lit = |n: (f32, f32, f32)| -> ([f32; 3], f32) {
            let diff = (n.0 * light.0 + n.1 * light.1 + n.2 * light.2).max(0.0);
            let hv = {
                let (hx, hy, hz) = (light.0, light.1, light.2 + 1.0);
                let l = (hx * hx + hy * hy + hz * hz).sqrt();
                (hx / l, hy / l, hz / l)
            };
            let spec = (n.0 * hv.0 + n.1 * hv.1 + n.2 * hv.2).max(0.0).powf(48.0) * 0.55;
            (shade(c, 0.28 + 0.8 * diff, spec), 1.0)
        };
        match kind {
            // sphere
            0 => {
                for y in bb.y0..=bb.y1 {
                    for x in bb.x0..=bb.x1 {
                        let nx = (x as f32 + 0.5 - cx) / rx;
                        let ny = (y as f32 + 0.5 - cy) / ry;
                        let rr = nx * nx + ny * ny;
                        let edge = clamp01((1.0 - rr.sqrt()) * rx.min(ry) + 0.5);
                        if edge <= 0.0 {
                            continue;
                        }
                        let nz = (1.0 - rr.min(1.0)).sqrt();
                        let (col, _) = lit((nx, ny, nz));
                        self.blend(x, y, col, edge);
                    }
                }
            }
            // cube (isometric)
            1 => {
                let top = vec![(cx, y0), (x1, y0 + h * 0.25), (cx, y0 + h * 0.5), (x0, y0 + h * 0.25)];
                let left = vec![(x0, y0 + h * 0.25), (cx, y0 + h * 0.5), (cx, y1), (x0, y1 - h * 0.25)];
                let right = vec![(cx, y0 + h * 0.5), (x1, y0 + h * 0.25), (x1, y1 - h * 0.25), (cx, y1)];
                let faces = [(top, shade(c, 1.1, 0.08)), (left, shade(c, 0.8, 0.0)), (right, shade(c, 0.55, 0.0))];
                for (pts, col) in faces.iter() {
                    for y in bb.y0..=bb.y1 {
                        for x in bb.x0..=bb.x1 {
                            let sd = polygon_sdf(pts, (x as f32 + 0.5, y as f32 + 0.5));
                            self.blend(x, y, *col, clamp01(0.5 - sd));
                        }
                    }
                }
            }
            // cylinder
            2 => {
                let ery = (h * 0.12).max(1.0);
                let (ty, by) = (y0 + ery, y1 - ery);
                for y in bb.y0..=bb.y1 {
                    for x in bb.x0..=bb.x1 {
                        let (px, py) = (x as f32 + 0.5, y as f32 + 0.5);
                        let nx = (px - cx) / rx;
                        let side_edge = clamp01((1.0 - nx.abs()) * rx + 0.5);
                        let in_body = py >= ty && py <= by;
                        let bot = (nx * nx + ((py - by) / ery).powi(2)).sqrt();
                        let top = (nx * nx + ((py - ty) / ery).powi(2)).sqrt();
                        let mut a = 0.0f32;
                        if in_body {
                            a = side_edge;
                        }
                        if py > by {
                            a = a.max(clamp01((1.0 - bot) * ery + 0.5));
                        }
                        if a > 0.0 {
                            let nz = (1.0 - (nx * nx).min(1.0)).sqrt();
                            let (col, _) = lit((nx, 0.0, nz));
                            self.blend(x, y, col, a);
                        }
                        let ta = clamp01((1.0 - top) * ery + 0.5);
                        if ta > 0.0 {
                            self.blend(x, y, shade(c, 1.05, 0.1), ta);
                        }
                    }
                }
            }
            // cone
            _ => {
                let ery = (h * 0.1).max(1.0);
                let by = y1 - ery;
                let tri = vec![(cx, y0), (x1, by), (x0, by)];
                for y in bb.y0..=bb.y1 {
                    for x in bb.x0..=bb.x1 {
                        let (px, py) = (x as f32 + 0.5, y as f32 + 0.5);
                        let mut a = clamp01(0.5 - polygon_sdf(&tri, (px, py)));
                        let nxb = (px - cx) / rx;
                        if py > by {
                            let e = (nxb * nxb + ((py - by) / ery).powi(2)).sqrt();
                            a = a.max(clamp01((1.0 - e) * ery + 0.5));
                        }
                        if a <= 0.0 {
                            continue;
                        }
                        let hw = (rx * ((py - y0) / (by - y0).max(1.0))).clamp(0.5, rx);
                        let nx = ((px - cx) / hw).clamp(-1.0, 1.0);
                        let nz = (1.0 - nx * nx).sqrt();
                        let k = w / (h + w);
                        let (col, _) = lit((nx * 0.9, -0.35 * k, nz * 0.9));
                        self.blend(x, y, col, a);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ fill / pick

    fn fill(&mut self, x: i32, y: i32, color: u32, tol: u32) -> bool {
        if x < 0 || y < 0 || x as usize >= self.w || y as usize >= self.h {
            return false;
        }
        let (c, a) = unpack(color);
        let (w, h) = (self.w, self.h);
        let i0 = (y as usize * w + x as usize) * 4;
        let target = [self.px[i0], self.px[i0 + 1], self.px[i0 + 2]];
        let tol = tol.min(255) as i32;
        let matches = |px: &[u8], i: usize| -> bool {
            let j = i * 4;
            (0..3).all(|k| (px[j + k] as i32 - target[k] as i32).abs() <= tol)
        };
        self.push_undo();
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
            self.blend(i % w, i / w, c, a);
        }
        true
    }

    fn pixel(&self, x: i32, y: i32) -> u32 {
        if x < 0 || y < 0 || x as usize >= self.w || y as usize >= self.h {
            return 0;
        }
        let i = (y as usize * self.w + x as usize) * 4;
        (self.px[i] as u32) << 24 | (self.px[i + 1] as u32) << 16 | (self.px[i + 2] as u32) << 8 | 255
    }

    // ---------------------------------------------------------------- effects

    fn effect(&mut self, kind: u32) {
        self.push_undo();
        let (w, h) = (self.w as f32, self.h as f32);
        for i in 0..self.w * self.h {
            let j = i * 4;
            let (r, g, b) = (self.px[j] as f32, self.px[j + 1] as f32, self.px[j + 2] as f32);
            let lum = 0.299 * r + 0.587 * g + 0.114 * b;
            let out = match kind {
                0 => [lum, lum, lum],
                1 => [lum * 1.07 + 20.0, lum * 0.95 + 8.0, lum * 0.75 - 10.0],
                2 => [r * 1.08 + 12.0, g * 1.02 + 4.0, b * 0.85],
                3 => [r * 0.85, g * 1.0 + 4.0, b * 1.1 + 14.0],
                4 => [255.0 - r, 255.0 - g, 255.0 - b],
                5 => [r * 1.15 + 15.0, g * 1.15 + 15.0, b * 1.15 + 15.0],
                6 => [r * 0.8, g * 0.8, b * 0.8],
                7 => [(r - 128.0) * 1.4 + 128.0, (g - 128.0) * 1.4 + 128.0, (b - 128.0) * 1.4 + 128.0],
                8 => {
                    let (x, y) = ((i % self.w) as f32 / w - 0.5, (i / self.w) as f32 / h - 0.5);
                    let k = 1.0 - (x * x + y * y) * 1.6;
                    [r * k, g * k, b * k]
                }
                _ => [r, g, b],
            };
            for k in 0..3 {
                self.px[j + k] = out[k].clamp(0.0, 255.0) as u8;
            }
        }
    }

    // ---------------------------------------------------------- canvas edits

    fn resize(&mut self, nw: usize, nh: usize, scale: bool) {
        let (nw, nh) = (nw.max(1), nh.max(1));
        self.push_undo();
        let mut out = vec![255u8; nw * nh * 4];
        if scale {
            let (sx, sy) = (self.w as f32 / nw as f32, self.h as f32 / nh as f32);
            for y in 0..nh {
                let fy = ((y as f32 + 0.5) * sy - 0.5).max(0.0);
                let (y0, ty) = (fy as usize, fy.fract());
                let y1 = (y0 + 1).min(self.h - 1);
                for x in 0..nw {
                    let fx = ((x as f32 + 0.5) * sx - 0.5).max(0.0);
                    let (x0, tx) = (fx as usize, fx.fract());
                    let x1 = (x0 + 1).min(self.w - 1);
                    for k in 0..3 {
                        let p = |xx: usize, yy: usize| self.px[(yy * self.w + xx) * 4 + k] as f32;
                        let top = p(x0, y0) * (1.0 - tx) + p(x1, y0) * tx;
                        let bot = p(x0, y1) * (1.0 - tx) + p(x1, y1) * tx;
                        out[(y * nw + x) * 4 + k] = (top * (1.0 - ty) + bot * ty + 0.5) as u8;
                    }
                }
            }
        } else {
            let ox = (nw as i64 - self.w as i64) / 2;
            let oy = (nh as i64 - self.h as i64) / 2;
            for y in 0..self.h {
                let ny = y as i64 + oy;
                if ny < 0 || ny >= nh as i64 {
                    continue;
                }
                for x in 0..self.w {
                    let nx = x as i64 + ox;
                    if nx < 0 || nx >= nw as i64 {
                        continue;
                    }
                    let (s, d) = ((y * self.w + x) * 4, (ny as usize * nw + nx as usize) * 4);
                    out[d..d + 4].copy_from_slice(&self.px[s..s + 4]);
                }
            }
        }
        self.replace(nw, nh, out);
    }

    fn crop(&mut self, x: i32, y: i32, cw: i32, ch: i32) -> bool {
        let x0 = x.max(0) as usize;
        let y0 = y.max(0) as usize;
        let x1 = ((x + cw).max(0) as usize).min(self.w);
        let y1 = ((y + ch).max(0) as usize).min(self.h);
        if x1 <= x0 || y1 <= y0 {
            return false;
        }
        self.push_undo();
        let (nw, nh) = (x1 - x0, y1 - y0);
        let mut out = vec![0u8; nw * nh * 4];
        for yy in 0..nh {
            let s = ((y0 + yy) * self.w + x0) * 4;
            out[yy * nw * 4..(yy + 1) * nw * 4].copy_from_slice(&self.px[s..s + nw * 4]);
        }
        self.replace(nw, nh, out);
        true
    }

    fn rotate(&mut self, cw: bool) {
        self.push_undo();
        let (w, h) = (self.w, self.h);
        let mut out = vec![0u8; w * h * 4];
        for y in 0..h {
            for x in 0..w {
                let (nx, ny) = if cw { (h - 1 - y, x) } else { (y, w - 1 - x) };
                let (s, d) = ((y * w + x) * 4, (ny * h + nx) * 4);
                out[d..d + 4].copy_from_slice(&self.px[s..s + 4]);
            }
        }
        self.replace(h, w, out);
    }

    fn flip(&mut self, horizontal: bool) {
        self.push_undo();
        let (w, h) = (self.w, self.h);
        let mut out = vec![0u8; w * h * 4];
        for y in 0..h {
            for x in 0..w {
                let (nx, ny) = if horizontal { (w - 1 - x, y) } else { (x, h - 1 - y) };
                let (s, d) = ((y * w + x) * 4, (ny * w + nx) * 4);
                out[d..d + 4].copy_from_slice(&self.px[s..s + 4]);
            }
        }
        self.px = out;
    }

    /// Composite a premultiplied BGRA image (e.g. rendered text or sticker).
    fn blend_bgra(&mut self, src: &[u8], sw: usize, sh: usize, dx: i32, dy: i32) {
        self.push_undo();
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
                let d = (ty as usize * self.w + tx as usize) * 4;
                let (b, g, r) = (src[s] as f32, src[s + 1] as f32, src[s + 2] as f32);
                for (k, v) in [r, g, b].iter().enumerate() {
                    self.px[d + k] = (v + self.px[d + k] as f32 * (1.0 - a) + 0.5).min(255.0) as u8;
                }
            }
        }
    }

    /// Replace the canvas with a straight-alpha RGBA image composited on white.
    fn load_rgba(&mut self, src: &[u8], w: usize, h: usize) {
        let mut out = vec![255u8; w * h * 4];
        for i in 0..w * h {
            let a = src[i * 4 + 3] as f32 / 255.0;
            for k in 0..3 {
                out[i * 4 + k] = (src[i * 4 + k] as f32 * a + 255.0 * (1.0 - a) + 0.5) as u8;
            }
        }
        self.replace(w, h, out);
        self.undo.clear();
        self.redo.clear();
    }
}

// -------------------------------------------------------------------- C ABI

unsafe fn cv<'a>(c: *mut Canvas) -> Option<&'a mut Canvas> {
    c.as_mut()
}

#[no_mangle]
pub extern "C" fn op3d_new(w: i32, h: i32) -> *mut Canvas {
    Box::into_raw(Box::new(Canvas::new(w.max(1) as usize, h.max(1) as usize)))
}

#[no_mangle]
pub unsafe extern "C" fn op3d_free(c: *mut Canvas) {
    if !c.is_null() {
        drop(Box::from_raw(c));
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_reset(c: *mut Canvas, w: i32, h: i32) {
    if let Some(c) = cv(c) {
        *c = Canvas::new(w.max(1) as usize, h.max(1) as usize);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_width(c: *mut Canvas) -> i32 {
    cv(c).map_or(0, |c| c.w as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_height(c: *mut Canvas) -> i32 {
    cv(c).map_or(0, |c| c.h as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_data(c: *mut Canvas) -> *const u8 {
    cv(c).map_or(std::ptr::null(), |c| c.px.as_ptr())
}

#[no_mangle]
pub unsafe extern "C" fn op3d_stroke_begin(c: *mut Canvas, tool: u32, color: u32, size: f32, opacity: f32, x: f32, y: f32) {
    if let Some(c) = cv(c) {
        c.stroke_begin(tool, color, size, opacity, x, y);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_stroke_to(c: *mut Canvas, x: f32, y: f32) {
    if let Some(c) = cv(c) {
        c.stroke_to(x, y);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_stroke_end(c: *mut Canvas) {
    if let Some(c) = cv(c) {
        c.stroke_end();
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_shape(c: *mut Canvas, kind: u32, x0: f32, y0: f32, x1: f32, y1: f32, stroke: u32, thick: f32, fill: u32) {
    if let Some(c) = cv(c) {
        c.shape(kind, x0, y0, x1, y1, stroke, thick, fill);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_model(c: *mut Canvas, kind: u32, x0: f32, y0: f32, x1: f32, y1: f32, color: u32) {
    if let Some(c) = cv(c) {
        c.model(kind, x0, y0, x1, y1, color);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_fill(c: *mut Canvas, x: i32, y: i32, color: u32, tol: u32) -> i32 {
    cv(c).map_or(0, |c| c.fill(x, y, color, tol) as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_pixel(c: *mut Canvas, x: i32, y: i32) -> u32 {
    cv(c).map_or(0, |c| c.pixel(x, y))
}

#[no_mangle]
pub unsafe extern "C" fn op3d_effect(c: *mut Canvas, kind: u32) {
    if let Some(c) = cv(c) {
        c.effect(kind);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_resize(c: *mut Canvas, w: i32, h: i32, scale: i32) {
    if let Some(c) = cv(c) {
        c.resize(w.max(1) as usize, h.max(1) as usize, scale != 0);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_crop(c: *mut Canvas, x: i32, y: i32, w: i32, h: i32) -> i32 {
    cv(c).map_or(0, |c| c.crop(x, y, w, h) as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_rotate(c: *mut Canvas, cw: i32) {
    if let Some(c) = cv(c) {
        c.rotate(cw != 0);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_flip(c: *mut Canvas, horizontal: i32) {
    if let Some(c) = cv(c) {
        c.flip(horizontal != 0);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_blend_bgra(c: *mut Canvas, src: *const u8, sw: i32, sh: i32, dx: i32, dy: i32) {
    if let Some(c) = cv(c) {
        if src.is_null() || sw <= 0 || sh <= 0 {
            return;
        }
        let s = slice::from_raw_parts(src, sw as usize * sh as usize * 4);
        c.blend_bgra(s, sw as usize, sh as usize, dx, dy);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_load_rgba(c: *mut Canvas, src: *const u8, w: i32, h: i32) {
    if let Some(c) = cv(c) {
        if src.is_null() || w <= 0 || h <= 0 {
            return;
        }
        let s = slice::from_raw_parts(src, w as usize * h as usize * 4);
        c.load_rgba(s, w as usize, h as usize);
    }
}

#[no_mangle]
pub unsafe extern "C" fn op3d_undo(c: *mut Canvas) -> i32 {
    cv(c).map_or(0, |c| c.undo() as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_redo(c: *mut Canvas) -> i32 {
    cv(c).map_or(0, |c| c.redo() as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_can_undo(c: *mut Canvas) -> i32 {
    cv(c).map_or(0, |c| !c.undo.is_empty() as i32)
}

#[no_mangle]
pub unsafe extern "C" fn op3d_can_redo(c: *mut Canvas) -> i32 {
    cv(c).map_or(0, |c| !c.redo.is_empty() as i32)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rgb(c: &Canvas, x: usize, y: usize) -> [u8; 3] {
        let i = (y * c.w + x) * 4;
        [c.px[i], c.px[i + 1], c.px[i + 2]]
    }

    #[test]
    fn stroke_paints_and_undo_restores() {
        let mut c = Canvas::new(64, 64);
        c.stroke_begin(TOOL_MARKER, 0xFF0000FF, 8.0, 1.0, 10.0, 32.0);
        c.stroke_to(50.0, 32.0);
        c.stroke_end();
        assert_eq!(rgb(&c, 30, 32), [255, 0, 0]);
        assert!(c.undo());
        assert_eq!(rgb(&c, 30, 32), [255, 255, 255]);
        assert!(c.redo());
        assert_eq!(rgb(&c, 30, 32), [255, 0, 0]);
    }

    #[test]
    fn opacity_does_not_accumulate_within_stroke() {
        let mut c = Canvas::new(64, 64);
        c.stroke_begin(TOOL_MARKER, 0x000000FF, 10.0, 0.5, 20.0, 32.0);
        for _ in 0..5 {
            c.stroke_to(40.0, 32.0);
            c.stroke_to(20.0, 32.0);
        }
        c.stroke_end();
        let v = rgb(&c, 30, 32)[0];
        assert!((126..=129).contains(&v), "got {v}");
    }

    #[test]
    fn shapes_fill_and_outline() {
        let mut c = Canvas::new(100, 100);
        c.shape(1, 10.0, 10.0, 90.0, 90.0, 0x000000FF, 2.0, 0x00FF00FF);
        assert_eq!(rgb(&c, 50, 50), [0, 255, 0]);
        assert_eq!(rgb(&c, 10, 50), [0, 0, 0]);
        assert_eq!(rgb(&c, 5, 5), [255, 255, 255]);
        c.shape(2, 0.0, 0.0, 100.0, 100.0, 0, 0.0, 0x0000FFFF);
        assert_eq!(rgb(&c, 50, 50), [0, 0, 255]);
        assert_eq!(rgb(&c, 2, 2), [255, 255, 255]);
    }

    #[test]
    fn flood_fill_stops_at_border() {
        let mut c = Canvas::new(50, 50);
        c.shape(1, 10.0, 10.0, 40.0, 40.0, 0x000000FF, 3.0, 0);
        c.fill(25, 25, 0xFF0000FF, 10);
        assert_eq!(rgb(&c, 25, 25), [255, 0, 0]);
        assert_eq!(rgb(&c, 2, 2), [255, 255, 255]);
    }

    #[test]
    fn models_and_canvas_ops() {
        let mut c = Canvas::new(80, 60);
        for k in 0..4 {
            c.model(k, 5.0, 5.0, 75.0, 55.0, 0x3366CCFF);
        }
        c.rotate(true);
        assert_eq!((c.w, c.h), (60, 80));
        c.resize(30, 40, true);
        assert_eq!((c.w, c.h), (30, 40));
        assert!(c.crop(5, 5, 10, 10));
        assert_eq!((c.w, c.h), (10, 10));
        for _ in 0..4 {
            c.undo();
        }
        assert_eq!((c.w, c.h), (80, 60));
    }
}
