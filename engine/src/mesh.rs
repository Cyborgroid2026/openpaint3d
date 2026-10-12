//! Triangle meshes: primitives in a unit box ([-0.5, 0.5]^3) and meshes
//! built from 2D coverage masks (extruded or inflated).

use crate::math::{v3, V3};
use std::collections::HashMap;
use std::f32::consts::{PI, TAU};

#[derive(Clone, Default, Debug)]
pub struct Mesh {
    pub pos: Vec<V3>,
    pub nrm: Vec<V3>,
    pub tri: Vec<[u32; 3]>,
}

// primitive ids shared with the C# side
pub const PRIM_CUBE: u32 = 0;
pub const PRIM_SPHERE: u32 = 1;
pub const PRIM_CYLINDER: u32 = 2;
pub const PRIM_CONE: u32 = 3;
pub const PRIM_PYRAMID: u32 = 4;
pub const PRIM_CAPSULE: u32 = 5;
pub const PRIM_TORUS: u32 = 6;
pub const PRIM_HEMISPHERE: u32 = 7;

impl Mesh {
    fn vert(&mut self, p: V3, n: V3) -> u32 {
        self.pos.push(p);
        self.nrm.push(n.norm());
        (self.pos.len() - 1) as u32
    }

    fn quad(&mut self, a: u32, b: u32, c: u32, d: u32) {
        self.tri.push([a, b, c]);
        self.tri.push([a, c, d]);
    }

    /// Flat-shaded polygon (convex, ordered) with a single normal.
    fn flat_poly(&mut self, pts: &[V3], n: V3) {
        let base = self.pos.len() as u32;
        for &p in pts {
            self.vert(p, n);
        }
        for i in 1..pts.len() as u32 - 1 {
            self.tri.push([base, base + i, base + i + 1]);
        }
    }

    pub fn primitive(kind: u32) -> Mesh {
        match kind {
            PRIM_CUBE => cube(),
            PRIM_SPHERE => sphere(48, 24, 1.0),
            PRIM_CYLINDER => cylinder(48),
            PRIM_CONE => cone(48),
            PRIM_PYRAMID => pyramid(),
            PRIM_CAPSULE => capsule(48),
            PRIM_TORUS => torus(48, 24),
            PRIM_HEMISPHERE => sphere(48, 12, 0.5),
            _ => cube(),
        }
    }

    pub fn bounds(&self) -> (V3, V3) {
        let mut lo = v3(f32::MAX, f32::MAX, f32::MAX);
        let mut hi = v3(f32::MIN, f32::MIN, f32::MIN);
        for p in &self.pos {
            lo = v3(lo.x.min(p.x), lo.y.min(p.y), lo.z.min(p.z));
            hi = v3(hi.x.max(p.x), hi.y.max(p.y), hi.z.max(p.z));
        }
        (lo, hi)
    }
}

fn cube() -> Mesh {
    let mut m = Mesh::default();
    let h = 0.5;
    let faces: [(V3, V3, V3); 6] = [
        (v3(0.0, 0.0, -1.0), v3(1.0, 0.0, 0.0), v3(0.0, 1.0, 0.0)),
        (v3(0.0, 0.0, 1.0), v3(-1.0, 0.0, 0.0), v3(0.0, 1.0, 0.0)),
        (v3(1.0, 0.0, 0.0), v3(0.0, 0.0, 1.0), v3(0.0, 1.0, 0.0)),
        (v3(-1.0, 0.0, 0.0), v3(0.0, 0.0, -1.0), v3(0.0, 1.0, 0.0)),
        (v3(0.0, -1.0, 0.0), v3(1.0, 0.0, 0.0), v3(0.0, 0.0, 1.0)),
        (v3(0.0, 1.0, 0.0), v3(1.0, 0.0, 0.0), v3(0.0, 0.0, -1.0)),
    ];
    for (n, u, w) in faces {
        let c = n * h;
        m.flat_poly(&[c - u * h - w * h, c + u * h - w * h, c + u * h + w * h, c - u * h + w * h], n);
    }
    m
}

/// UV sphere of radius 0.5; `portion` < 1 keeps only the top part (y < 0 is up).
fn sphere(seg: u32, rings: u32, portion: f32) -> Mesh {
    let mut m = Mesh::default();
    for r in 0..=rings {
        let phi = PI * portion * r as f32 / rings as f32;
        for s in 0..=seg {
            let th = TAU * s as f32 / seg as f32;
            let n = v3(phi.sin() * th.cos(), -phi.cos(), phi.sin() * th.sin());
            m.vert(n * 0.5, n);
        }
    }
    let row = seg + 1;
    for r in 0..rings {
        for s in 0..seg {
            let a = r * row + s;
            m.quad(a, a + 1, a + row + 1, a + row);
        }
    }
    if portion < 1.0 {
        // flat cap closing the dome
        let y = -(PI * portion).cos() * 0.5;
        let rad = (PI * portion).sin() * 0.5;
        let pts: Vec<V3> = (0..seg).map(|s| {
            let th = TAU * s as f32 / seg as f32;
            v3(rad * th.cos(), y, rad * th.sin())
        }).collect();
        m.flat_poly(&pts, v3(0.0, 1.0, 0.0));
    }
    m
}

fn ring(seg: u32, r: f32, y: f32) -> Vec<V3> {
    (0..seg)
        .map(|s| {
            let th = TAU * s as f32 / seg as f32;
            v3(r * th.cos(), y, r * th.sin())
        })
        .collect()
}

fn cylinder(seg: u32) -> Mesh {
    let mut m = Mesh::default();
    for s in 0..=seg {
        let th = TAU * s as f32 / seg as f32;
        let n = v3(th.cos(), 0.0, th.sin());
        m.vert(v3(0.5 * th.cos(), -0.5, 0.5 * th.sin()), n);
        m.vert(v3(0.5 * th.cos(), 0.5, 0.5 * th.sin()), n);
    }
    for s in 0..seg {
        let a = s * 2;
        m.quad(a, a + 2, a + 3, a + 1);
    }
    m.flat_poly(&ring(seg, 0.5, -0.5), v3(0.0, -1.0, 0.0));
    m.flat_poly(&ring(seg, 0.5, 0.5), v3(0.0, 1.0, 0.0));
    m
}

fn cone(seg: u32) -> Mesh {
    let mut m = Mesh::default();
    // slope normal: radius 0.5 over height 1
    for s in 0..=seg {
        let th = TAU * s as f32 / seg as f32;
        let n = v3(th.cos(), -0.5, th.sin());
        let thm = TAU * (s as f32 + 0.5) / seg as f32;
        m.vert(v3(0.0, -0.5, 0.0), v3(thm.cos(), -0.5, thm.sin()));
        m.vert(v3(0.5 * th.cos(), 0.5, 0.5 * th.sin()), n);
    }
    for s in 0..seg {
        let a = s * 2;
        m.tri.push([a, a + 3, a + 1]);
    }
    m.flat_poly(&ring(seg, 0.5, 0.5), v3(0.0, 1.0, 0.0));
    m
}

fn pyramid() -> Mesh {
    let mut m = Mesh::default();
    let apex = v3(0.0, -0.5, 0.0);
    let b = [v3(-0.5, 0.5, -0.5), v3(0.5, 0.5, -0.5), v3(0.5, 0.5, 0.5), v3(-0.5, 0.5, 0.5)];
    for i in 0..4 {
        let (p, q) = (b[i], b[(i + 1) % 4]);
        let n = (q - p).cross(apex - p).norm();
        let n = if n.dot((p + q) * 0.5) < 0.0 { -n } else { n };
        m.flat_poly(&[p, q, apex], n);
    }
    m.flat_poly(&b, v3(0.0, 1.0, 0.0));
    m
}

fn capsule(seg: u32) -> Mesh {
    let mut m = Mesh::default();
    let r = 0.25;
    let rings = 12;
    let row = seg + 1;
    // top hemisphere, cylinder, bottom hemisphere as one lathe
    let mut profile: Vec<(f32, f32, f32, f32)> = Vec::new(); // (radius, y, n_r, n_y)
    for i in 0..=rings {
        let phi = PI / 2.0 * i as f32 / rings as f32;
        profile.push((r * phi.sin(), -0.25 - r * phi.cos(), phi.sin(), -phi.cos()));
    }
    for i in 0..=rings {
        let phi = PI / 2.0 + PI / 2.0 * i as f32 / rings as f32;
        profile.push((r * phi.sin(), 0.25 - r * phi.cos(), phi.sin(), -phi.cos()));
    }
    for &(rad, y, nr, ny) in &profile {
        for s in 0..=seg {
            let th = TAU * s as f32 / seg as f32;
            m.vert(v3(rad * th.cos(), y, rad * th.sin()), v3(nr * th.cos(), ny, nr * th.sin()));
        }
    }
    for i in 0..profile.len() as u32 - 1 {
        for s in 0..seg {
            let a = i * row + s;
            m.quad(a, a + 1, a + row + 1, a + row);
        }
    }
    // scale so the capsule fills the unit box horizontally
    for p in &mut m.pos {
        p.x *= 2.0;
        p.z *= 2.0;
    }
    for n in &mut m.nrm {
        *n = v3(n.x * 0.5, n.y, n.z * 0.5).norm();
    }
    m
}

fn torus(seg: u32, sides: u32) -> Mesh {
    let mut m = Mesh::default();
    let (big, small) = (0.35, 0.15);
    for i in 0..=seg {
        let th = TAU * i as f32 / seg as f32;
        let c = v3(th.cos(), 0.0, th.sin());
        for j in 0..=sides {
            let ph = TAU * j as f32 / sides as f32;
            let n = c * ph.cos() + v3(0.0, ph.sin(), 0.0);
            m.vert(c * big + n * small, n);
        }
    }
    let row = sides + 1;
    for i in 0..seg {
        for j in 0..sides {
            let a = i * row + j;
            m.quad(a, a + row, a + row + 1, a + 1);
        }
    }
    m
}

/// A flat quad on z = 0 covering [0,w]x[0,h] (used for the canvas in 3D view).
pub fn canvas_quad(w: f32, h: f32) -> Mesh {
    let mut m = Mesh::default();
    m.flat_poly(&[v3(0.0, 0.0, 0.0), v3(w, 0.0, 0.0), v3(w, h, 0.0), v3(0.0, h, 0.0)], v3(0.0, 0.0, -1.0));
    m
}

// ------------------------------------------------------------------ masks

pub struct Mask<'a> {
    pub w: usize,
    pub h: usize,
    pub a: &'a [u8],
}

impl Mask<'_> {
    fn at(&self, x: i64, y: i64) -> f32 {
        if x < 0 || y < 0 || x >= self.w as i64 || y >= self.h as i64 {
            0.0
        } else {
            self.a[y as usize * self.w + x as usize] as f32 / 255.0
        }
    }
}

/// Marching-squares grid over a mask, padded by one cell so contours close.
struct Grid {
    gw: usize,
    gh: usize,
    step: f32,
    val: Vec<f32>,
    ox: f32,
    oy: f32,
    mw: f32,
    mh: f32,
}

impl Grid {
    fn new(mask: &Mask, step: usize) -> Grid {
        let step = step.max(1);
        let gw = mask.w / step + 3;
        let gh = mask.h / step + 3;
        let mut val = vec![0.0; gw * gh];
        for gy in 0..gh {
            for gx in 0..gw {
                // grid point (gx, gy) maps to mask pixel ((gx-1)*step, (gy-1)*step)
                let mx = (gx as i64 - 1) * step as i64;
                let my = (gy as i64 - 1) * step as i64;
                // average a step x step block for smoother iso lines
                let mut s = 0.0;
                let mut n = 0.0;
                let half = step as i64 / 2;
                for dy in -half..=half {
                    for dx in -half..=half {
                        s += mask.at(mx + dx, my + dy);
                        n += 1.0;
                    }
                }
                val[gy * gw + gx] = s / n;
            }
        }
        Grid { gw, gh, step: step as f32, val, ox: -1.0 * step as f32, oy: -1.0 * step as f32, mw: mask.w as f32, mh: mask.h as f32 }
    }

    fn v(&self, gx: usize, gy: usize) -> f32 {
        self.val[gy * self.gw + gx]
    }

    /// Unit-space x/y for a mask-space position.
    fn unit(&self, mx: f32, my: f32) -> (f32, f32) {
        (mx / self.mw.max(1.0) - 0.5, my / self.mh.max(1.0) - 0.5)
    }

    fn corner(&self, gx: usize, gy: usize) -> (f32, f32) {
        self.unit(self.ox + gx as f32 * self.step, self.oy + gy as f32 * self.step)
    }

    /// Crossing point on the edge between two grid corners.
    fn cross(&self, a: (usize, usize), b: (usize, usize)) -> (f32, f32) {
        let (va, vb) = (self.v(a.0, a.1), self.v(b.0, b.1));
        let t = if (vb - va).abs() > 1e-6 { ((0.5 - va) / (vb - va)).clamp(0.0, 1.0) } else { 0.5 };
        let pa = self.corner(a.0, a.1);
        let pb = self.corner(b.0, b.1);
        (pa.0 + (pb.0 - pa.0) * t, pa.1 + (pb.1 - pa.1) * t)
    }

    fn edge_id(&self, a: (usize, usize), b: (usize, usize)) -> usize {
        let (p, horizontal) = if a.1 == b.1 { (if a.0 < b.0 { a } else { b }, true) } else { (if a.1 < b.1 { a } else { b }, false) };
        (p.1 * self.gw + p.0) * 2 + if horizontal { 0 } else { 1 }
    }
}

#[derive(Clone, Copy, PartialEq)]
enum PolyPt {
    Corner(usize, usize),
    Edge((usize, usize), (usize, usize)),
}

/// Inside polygon of one marching-squares cell, walking tl -> tr -> br -> bl.
fn cell_polygon(g: &Grid, gx: usize, gy: usize) -> Vec<PolyPt> {
    let c = [(gx, gy), (gx + 1, gy), (gx + 1, gy + 1), (gx, gy + 1)];
    let inside: Vec<bool> = c.iter().map(|&(x, y)| g.v(x, y) >= 0.5).collect();
    let mut out = Vec::new();
    for i in 0..4 {
        let j = (i + 1) % 4;
        if inside[i] {
            out.push(PolyPt::Corner(c[i].0, c[i].1));
        }
        if inside[i] != inside[j] {
            out.push(PolyPt::Edge(c[i], c[j]));
        }
    }
    out
}

/// Boundary segments of one cell, oriented with the inside on the left.
fn cell_segments(g: &Grid, gx: usize, gy: usize) -> Vec<(PolyPt, PolyPt)> {
    let poly = cell_polygon(g, gx, gy);
    let n = poly.len();
    let mut segs = Vec::new();
    for i in 0..n {
        let (a, b) = (poly[i], poly[(i + 1) % n]);
        if let (PolyPt::Edge(..), PolyPt::Edge(..)) = (a, b) {
            segs.push((a, b));
        }
    }
    segs
}

/// Extrude a mask into a slab: front at z = -0.5, back at z = +0.5.
pub fn extrude(mask: &Mask, step: usize) -> Mesh {
    let g = Grid::new(mask, step);
    let mut m = Mesh::default();

    for (z, nz) in [(-0.5f32, -1.0f32), (0.5, 1.0)] {
        for gy in 0..g.gh - 1 {
            let mut gx = 0;
            while gx < g.gw - 1 {
                let poly = cell_polygon(&g, gx, gy);
                let full = poly.len() == 4 && poly.iter().all(|p| matches!(p, PolyPt::Corner(..)));
                if full {
                    // merge a run of full cells into one quad
                    let start = gx;
                    while gx + 1 < g.gw - 1 {
                        let next = cell_polygon(&g, gx + 1, gy);
                        if next.len() == 4 && next.iter().all(|p| matches!(p, PolyPt::Corner(..))) {
                            gx += 1;
                        } else {
                            break;
                        }
                    }
                    let (x0, y0) = g.corner(start, gy);
                    let (x1, y1) = g.corner(gx + 1, gy + 1);
                    let pts = [v3(x0, y0, z), v3(x1, y0, z), v3(x1, y1, z), v3(x0, y1, z)];
                    m.flat_poly(&pts, v3(0.0, 0.0, nz));
                } else if poly.len() >= 3 {
                    let pts: Vec<V3> = poly
                        .iter()
                        .map(|p| {
                            let (x, y) = match *p {
                                PolyPt::Corner(cx, cy) => g.corner(cx, cy),
                                PolyPt::Edge(a, b) => g.cross(a, b),
                            };
                            v3(x, y, z)
                        })
                        .collect();
                    m.flat_poly(&pts, v3(0.0, 0.0, nz));
                }
                gx += 1;
            }
        }
    }

    // side walls with smooth normals shared along the contour
    let mut cpos: HashMap<usize, (f32, f32)> = HashMap::new();
    let mut cn: HashMap<usize, V3> = HashMap::new();
    let mut segs: Vec<(usize, usize)> = Vec::new();
    for gy in 0..g.gh - 1 {
        for gx in 0..g.gw - 1 {
            for (a, b) in cell_segments(&g, gx, gy) {
                let (PolyPt::Edge(a0, a1), PolyPt::Edge(b0, b1)) = (a, b) else { continue };
                let (ida, idb) = (g.edge_id(a0, a1), g.edge_id(b0, b1));
                let (pa, pb) = (g.cross(a0, a1), g.cross(b0, b1));
                cpos.insert(ida, pa);
                cpos.insert(idb, pb);
                // the cell walk keeps the inside on the (mathematical) left, so outward is to the right
                let out = v3(pb.1 - pa.1, -(pb.0 - pa.0), 0.0);
                for id in [ida, idb] {
                    let e = cn.entry(id).or_default();
                    *e = *e + out;
                }
                segs.push((ida, idb));
            }
        }
    }
    let mut index: HashMap<usize, (u32, u32)> = HashMap::new();
    for (&id, &(x, y)) in &cpos {
        let n = cn.get(&id).copied().unwrap_or(v3(1.0, 0.0, 0.0));
        let f = m.vert(v3(x, y, -0.5), n);
        let b = m.vert(v3(x, y, 0.5), n);
        index.insert(id, (f, b));
    }
    for &(ia, ib) in &segs {
        let ((fa, ba), (fb, bb)) = (index[&ia], index[&ib]);
        m.quad(fa, fb, bb, ba);
    }
    m
}

/// Inflate a mask into a pillow-like solid (soft 3D doodle).
pub fn inflate(mask: &Mask, step: usize) -> Mesh {
    let g = Grid::new(mask, step);
    // distance (in grid cells) from each inside grid point to the outside
    let (gw, gh) = (g.gw, g.gh);
    let big = 1e9f32;
    let mut d: Vec<f32> = (0..gw * gh).map(|i| if g.val[i] >= 0.5 { big } else { 0.0 }).collect();
    // two-pass chamfer distance
    let (a, b) = (1.0f32, std::f32::consts::SQRT_2);
    for y in 0..gh {
        for x in 0..gw {
            let i = y * gw + x;
            if d[i] == 0.0 {
                continue;
            }
            let mut v = d[i];
            if x > 0 { v = v.min(d[i - 1] + a); }
            if y > 0 { v = v.min(d[i - gw] + a); }
            if x > 0 && y > 0 { v = v.min(d[i - gw - 1] + b); }
            if x + 1 < gw && y > 0 { v = v.min(d[i - gw + 1] + b); }
            d[i] = v;
        }
    }
    for y in (0..gh).rev() {
        for x in (0..gw).rev() {
            let i = y * gw + x;
            if d[i] == 0.0 {
                continue;
            }
            let mut v = d[i];
            if x + 1 < gw { v = v.min(d[i + 1] + a); }
            if y + 1 < gh { v = v.min(d[i + gw] + a); }
            if x + 1 < gw && y + 1 < gh { v = v.min(d[i + gw + 1] + b); }
            if x > 0 && y + 1 < gh { v = v.min(d[i + gw - 1] + b); }
            d[i] = v;
        }
    }
    let maxd = d.iter().cloned().fold(0.0f32, f32::max).max(1.0);
    let height = |gx: usize, gy: usize| -> f32 {
        let t = (d[gy * gw + gx] / maxd).min(1.0);
        0.5 * (1.0 - (1.0 - t) * (1.0 - t)).sqrt()
    };

    let mut m = Mesh::default();
    for side in [-1.0f32, 1.0] {
        let mut corner_idx: HashMap<(usize, usize), u32> = HashMap::new();
        let mut edge_idx: HashMap<usize, u32> = HashMap::new();
        let start_tri = m.tri.len();
        let start_vert = m.pos.len();
        for gy in 0..gh - 1 {
            for gx in 0..gw - 1 {
                let poly = cell_polygon(&g, gx, gy);
                if poly.len() < 3 {
                    continue;
                }
                let ids: Vec<u32> = poly
                    .iter()
                    .map(|p| match *p {
                        PolyPt::Corner(cx, cy) => *corner_idx.entry((cx, cy)).or_insert_with(|| {
                            let (x, y) = g.corner(cx, cy);
                            let h = height(cx, cy);
                            m.pos.push(v3(x, y, side * h));
                            m.nrm.push(V3::default());
                            (m.pos.len() - 1) as u32
                        }),
                        PolyPt::Edge(a, b) => *edge_idx.entry(g.edge_id(a, b)).or_insert_with(|| {
                            let (x, y) = g.cross(a, b);
                            m.pos.push(v3(x, y, 0.0));
                            m.nrm.push(V3::default());
                            (m.pos.len() - 1) as u32
                        }),
                    })
                    .collect();
                for i in 1..ids.len() - 1 {
                    if side < 0.0 {
                        m.tri.push([ids[0], ids[i + 1], ids[i]]);
                    } else {
                        m.tri.push([ids[0], ids[i], ids[i + 1]]);
                    }
                }
            }
        }
        // smooth normals for this side
        for t in &m.tri[start_tri..] {
            let (p0, p1, p2) = (m.pos[t[0] as usize], m.pos[t[1] as usize], m.pos[t[2] as usize]);
            let n = (p1 - p0).cross(p2 - p0);
            for &k in t {
                m.nrm[k as usize] = m.nrm[k as usize] + n;
            }
        }
        for n in &mut m.nrm[start_vert..] {
            *n = n.norm();
        }
    }
    m
}

#[cfg(test)]
mod tests {
    use super::*;

    fn disc(n: usize) -> Vec<u8> {
        let c = n as f32 / 2.0;
        (0..n * n)
            .map(|i| {
                let (x, y) = ((i % n) as f32 + 0.5 - c, (i / n) as f32 + 0.5 - c);
                if (x * x + y * y).sqrt() < c * 0.8 { 255 } else { 0 }
            })
            .collect()
    }

    #[test]
    fn primitives_fit_unit_box() {
        for k in 0..8 {
            let m = Mesh::primitive(k);
            assert!(!m.tri.is_empty());
            let (lo, hi) = m.bounds();
            for v in [lo.x, lo.y, lo.z, hi.x, hi.y, hi.z] {
                assert!(v.abs() <= 0.5001, "prim {k} out of box: {lo:?} {hi:?}");
            }
        }
    }

    #[test]
    fn extrude_and_inflate_disc() {
        let a = disc(64);
        let mask = Mask { w: 64, h: 64, a: &a };
        let e = extrude(&mask, 2);
        assert!(e.tri.len() > 50);
        let (lo, hi) = e.bounds();
        assert!((lo.z + 0.5).abs() < 1e-5 && (hi.z - 0.5).abs() < 1e-5);
        assert!(hi.x < 0.45 && lo.x > -0.45);
        // side normal at the rightmost contour point faces +x
        let (k, _) = e.pos.iter().enumerate().max_by(|a, b| a.1.x.partial_cmp(&b.1.x).unwrap()).unwrap();
        assert!(e.nrm[k].x > 0.5 || e.nrm[k].z.abs() > 0.99, "{:?}", e.nrm[k]);
        let i = inflate(&mask, 2);
        let (lo, hi) = i.bounds();
        assert!(hi.z > 0.3 && lo.z < -0.3);
        // front normals near the centre should face the viewer (-z)
        let c = i.pos.iter().enumerate().filter(|(_, p)| p.z < -0.45).map(|(k, _)| i.nrm[k]).next().unwrap();
        assert!(c.z < -0.8, "{c:?}");
    }
}
