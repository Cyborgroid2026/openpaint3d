//! 3D objects, lighting and a z-buffered software rasterizer.

use crate::math::{clamp01, v3, M3, V3};
use crate::mesh::Mesh;
use std::sync::Arc;

pub const FINISH_MATTE: u32 = 0;
pub const FINISH_GLOSS: u32 = 1;
pub const FINISH_DULL_METAL: u32 = 2;
pub const FINISH_POLISHED_METAL: u32 = 3;

#[derive(Clone)]
pub struct Part {
    pub mesh: Arc<Mesh>,
    /// local linear transform (rotation * scale) inside the object's unit box
    pub m: M3,
    pub off: V3,
    /// None = use the object's colour
    pub color: Option<[f32; 3]>,
}

#[derive(Clone)]
pub struct Obj {
    pub id: u32,
    pub parts: Arc<Vec<Part>>,
    pub pos: V3,
    pub rot: M3,
    pub scale: V3,
    pub color: [f32; 3],
    pub finish: u32,
    /// objects sharing a non-zero group id are selected and moved together
    pub group: u32,
}

impl Obj {
    pub fn linear(&self) -> M3 {
        self.rot.mul(&M3::scale(self.scale))
    }

    /// World-space corners of the object's unit box.
    pub fn corners(&self) -> [V3; 8] {
        let l = self.linear();
        let mut out = [V3::default(); 8];
        for (i, c) in out.iter_mut().enumerate() {
            let p = v3(
                if i & 1 == 0 { -0.5 } else { 0.5 },
                if i & 2 == 0 { -0.5 } else { 0.5 },
                if i & 4 == 0 { -0.5 } else { 0.5 },
            );
            *c = l.apply(p) + self.pos;
        }
        out
    }
}

#[derive(Clone, Copy)]
pub struct Light {
    /// direction from a surface towards the light
    pub dir: V3,
    pub color: [f32; 3],
    pub intensity: f32,
    pub ambient: f32,
}

impl Default for Light {
    fn default() -> Self {
        Light::from_angle(0.0, [1.0; 3], 1.0, 0.35)
    }
}

impl Light {
    /// `angle` rotates the default top-left light around the view axis (degrees).
    pub fn from_angle(angle: f32, color: [f32; 3], intensity: f32, ambient: f32) -> Light {
        let base = v3(-0.45, -0.6, -0.66);
        let r = M3::rot_axis(2, angle);
        Light { dir: r.apply(base).norm(), color, intensity, ambient }
    }
}

#[derive(Clone, Copy)]
pub struct Camera {
    pub eye: V3,
    pub r: V3,
    pub u: V3,
    pub f: V3,
    pub focal: f32,
    pub cx: f32,
    pub cy: f32,
}

impl Camera {
    pub fn look_at(eye: V3, target: V3, up: V3, focal: f32, cx: f32, cy: f32) -> Camera {
        let f = (target - eye).norm();
        let r = f.cross(up).norm();
        let u = r.cross(f);
        Camera { eye, r, u, f, focal, cx, cy }
    }

    /// Camera that maps the z = 0 plane 1:1 onto a w x h canvas, scaled by `ss`.
    pub fn canvas(w: f32, h: f32, ss: f32) -> Camera {
        let d = 2.5 * w.max(h);
        let eye = v3(w / 2.0, h / 2.0, -d);
        Camera::look_at(eye, v3(w / 2.0, h / 2.0, 0.0), v3(0.0, -1.0, 0.0), d * ss, w * ss / 2.0, h * ss / 2.0)
    }

    /// Orbit camera around the canvas centre for the 3D view.
    /// Without `perspective` the camera backs far away with a matching long lens, which is
    /// visually orthographic.
    #[allow(clippy::too_many_arguments)]
    pub fn orbit(w: f32, h: f32, yaw: f32, pitch: f32, zoom: f32, out_w: f32, out_h: f32, perspective: bool) -> Camera {
        let target = v3(w / 2.0, h / 2.0, 0.0);
        let lens = if perspective { 1.0 } else { 40.0 };
        let dist = 1.6 * w.max(h) / zoom.max(0.05) * lens;
        let (sy, cy) = yaw.to_radians().sin_cos();
        let (sp, cp) = pitch.to_radians().sin_cos();
        let dir = v3(sy * cp, sp, -cy * cp);
        let fov = 40f32.to_radians();
        let focal = (out_h / 2.0) / (fov / 2.0).tan() * lens;
        Camera::look_at(target + dir * dist, target, v3(0.0, -1.0, 0.0), focal, out_w / 2.0, out_h / 2.0)
    }

    /// (screen x, screen y, view depth)
    pub fn project(&self, p: V3) -> (f32, f32, f32) {
        let d = p - self.eye;
        let (x, y, z) = (d.dot(self.r), d.dot(self.u), d.dot(self.f));
        let z = z.max(1e-3);
        (self.cx + x / z * self.focal, self.cy - y / z * self.focal, z)
    }
}

/// Simple sky/ground environment used for metallic reflections.
fn env(r: V3, light: &Light) -> [f32; 3] {
    let t = -r.y; // up is -y
    let (sky, hor, gnd) = ([0.95, 0.97, 1.0], [0.72, 0.74, 0.78], [0.22, 0.21, 0.2]);
    let c = if t > 0.0 {
        let k = t.min(1.0);
        [hor[0] + (sky[0] - hor[0]) * k, hor[1] + (sky[1] - hor[1]) * k, hor[2] + (sky[2] - hor[2]) * k]
    } else {
        let k = (-t * 2.5).min(1.0);
        [hor[0] + (gnd[0] - hor[0]) * k, hor[1] + (gnd[1] - hor[1]) * k, hor[2] + (gnd[2] - hor[2]) * k]
    };
    [c[0] * light.color[0], c[1] * light.color[1], c[2] * light.color[2]]
}

pub fn shade(base: [f32; 3], finish: u32, n: V3, view: V3, light: &Light) -> [f32; 3] {
    let l = light.dir;
    let dif = n.dot(l).max(0.0) * light.intensity;
    let h = (l + view).norm();
    let nh = n.dot(h).max(0.0);
    let refl = (n * (2.0 * n.dot(view)) - view).norm();
    let lc = light.color;
    let amb = light.ambient;
    let mut out = [0.0f32; 3];
    for k in 0..3 {
        let b = base[k];
        let lit = amb + dif * lc[k];
        out[k] = match finish {
            FINISH_GLOSS => {
                let e = env(refl, light)[k];
                b * lit * 0.92 + nh.powf(70.0) * 0.75 * lc[k] * light.intensity + e * 0.08
            }
            FINISH_DULL_METAL => {
                let e = env(refl, light)[k];
                b * (amb * 0.7 + dif * lc[k] * 0.5) + b * e * 0.45 + nh.powf(18.0) * 0.35 * (0.5 * b + 0.5) * lc[k]
            }
            FINISH_POLISHED_METAL => {
                let e = env(refl, light)[k];
                b * lit * 0.12 + b * e * 0.85 + nh.powf(140.0) * 1.0 * (0.4 * b + 0.6) * lc[k] * light.intensity
            }
            _ => b * lit + nh.powf(8.0) * 0.04,
        };
    }
    [clamp01(out[0]), clamp01(out[1]), clamp01(out[2])]
}

/// Textured plane drawn behind the objects in the 3D view.
pub struct Plane<'a> {
    pub mesh: &'a Mesh,
    pub tex: &'a [u8],
    pub tw: usize,
    pub th: usize,
}

pub struct Target<'a> {
    pub w: usize,
    pub h: usize,
    /// first row of this band in the full image (bands render in parallel)
    pub y0: usize,
    /// premultiplied RGBA, 0..1
    pub color: &'a mut [[f32; 4]],
    /// 1 / view depth (bigger is nearer), 0 = empty
    pub depth: &'a mut [f32],
    pub id: &'a mut [u32],
}

struct Vtx {
    sx: f32,
    sy: f32,
    iz: f32,
    world: V3,
    n: V3,
}

/// Render into the full target, split into horizontal bands across threads.
pub fn render(objs: &[Obj], cam: &Camera, light: &Light, plane: Option<&Plane>, t: &mut Target) {
    let threads = std::thread::available_parallelism().map_or(1, |n| n.get()).min(16);
    let rows = t.h.div_ceil(threads).max(16);
    let w = t.w;
    let full_h = t.h;
    std::thread::scope(|s| {
        let mut color: &mut [[f32; 4]] = t.color;
        let mut depth: &mut [f32] = t.depth;
        let mut id: &mut [u32] = t.id;
        let mut y = 0;
        while y < full_h {
            let n = rows.min(full_h - y);
            let (c0, c1) = std::mem::take(&mut color).split_at_mut(n * w);
            let (d0, d1) = std::mem::take(&mut depth).split_at_mut(n * w);
            let (i0, i1) = std::mem::take(&mut id).split_at_mut(n * w);
            color = c1;
            depth = d1;
            id = i1;
            let y0 = y;
            s.spawn(move || {
                let mut band = Target { w, h: n, y0, color: c0, depth: d0, id: i0 };
                render_band(objs, cam, light, plane, &mut band);
            });
            y += n;
        }
    });
}

fn render_band(objs: &[Obj], cam: &Camera, light: &Light, plane: Option<&Plane>, t: &mut Target) {
    if let Some(p) = plane {
        let verts: Vec<Vtx> = p
            .mesh
            .pos
            .iter()
            .zip(&p.mesh.nrm)
            .map(|(&pos, &n)| {
                let (sx, sy, z) = cam.project(pos);
                Vtx { sx, sy, iz: 1.0 / z, world: pos, n }
            })
            .collect();
        for tri in &p.mesh.tri {
            raster(t, cam, &verts, tri, 0, &|world, n, view| {
                let u = (world.x / p.tw.max(1) as f32).clamp(0.0, 1.0);
                let v = (world.y / p.th.max(1) as f32).clamp(0.0, 1.0);
                let tx = ((u * p.tw as f32) as usize).min(p.tw - 1);
                let ty = ((v * p.th as f32) as usize).min(p.th - 1);
                let i = (ty * p.tw + tx) * 4;
                let c = [p.tex[i] as f32 / 255.0, p.tex[i + 1] as f32 / 255.0, p.tex[i + 2] as f32 / 255.0];
                let dif = n.dot(light.dir).abs() * light.intensity;
                let k = 0.82 + 0.18 * dif;
                let _ = view;
                [c[0] * k * (0.6 + 0.4 * light.color[0]), c[1] * k * (0.6 + 0.4 * light.color[1]), c[2] * k * (0.6 + 0.4 * light.color[2])]
            });
        }
    }
    for o in objs {
        let ol = o.linear();
        for part in o.parts.iter() {
            let lin = ol.mul(&part.m);
            let nm = lin.normal_matrix();
            let base = part.color.unwrap_or(o.color);
            let verts: Vec<Vtx> = part
                .mesh
                .pos
                .iter()
                .zip(&part.mesh.nrm)
                .map(|(&p, &n)| {
                    let world = ol.apply(part.m.apply(p) + part.off) + o.pos;
                    let (sx, sy, z) = cam.project(world);
                    Vtx { sx, sy, iz: 1.0 / z, world, n: nm.apply(n).norm() }
                })
                .collect();
            for tri in &part.mesh.tri {
                raster(t, cam, &verts, tri, o.id, &|_, n, view| shade(base, o.finish, n, view, light));
            }
        }
    }
}

fn edge(a: (f32, f32), b: (f32, f32), p: (f32, f32)) -> f32 {
    (b.0 - a.0) * (p.1 - a.1) - (b.1 - a.1) * (p.0 - a.0)
}

fn raster(t: &mut Target, cam: &Camera, v: &[Vtx], tri: &[u32; 3], id: u32, shader: &dyn Fn(V3, V3, V3) -> [f32; 3]) {
    let (a, b, c) = (&v[tri[0] as usize], &v[tri[1] as usize], &v[tri[2] as usize]);
    if a.iz > 1e2 || b.iz > 1e2 || c.iz > 1e2 {
        return; // behind / too close to the camera
    }
    let (pa, pb, pc) = ((a.sx, a.sy), (b.sx, b.sy), (c.sx, c.sy));
    let area = edge(pa, pb, pc);
    if area.abs() < 1e-9 {
        return;
    }
    let x0 = a.sx.min(b.sx).min(c.sx).floor().max(0.0) as i64;
    let x1 = a.sx.max(b.sx).max(c.sx).ceil().min(t.w as f32 - 1.0) as i64;
    let y0 = a.sy.min(b.sy).min(c.sy).floor().max(t.y0 as f32) as i64;
    let y1 = a.sy.max(b.sy).max(c.sy).ceil().min((t.y0 + t.h) as f32 - 1.0) as i64;
    if x0 > x1 || y0 > y1 {
        return;
    }
    let inv = 1.0 / area;
    for py in y0..=y1 {
        for px in x0..=x1 {
            let p = (px as f32 + 0.5, py as f32 + 0.5);
            let w0 = edge(pb, pc, p) * inv;
            let w1 = edge(pc, pa, p) * inv;
            let w2 = 1.0 - w0 - w1;
            if w0 < 0.0 || w1 < 0.0 || w2 < 0.0 {
                continue;
            }
            let iz = w0 * a.iz + w1 * b.iz + w2 * c.iz;
            let i = (py as usize - t.y0) * t.w + px as usize;
            if iz <= t.depth[i] {
                continue;
            }
            let (k0, k1, k2) = (w0 * a.iz / iz, w1 * b.iz / iz, w2 * c.iz / iz);
            let world = a.world * k0 + b.world * k1 + c.world * k2;
            let mut n = (a.n * k0 + b.n * k1 + c.n * k2).norm();
            let view = (cam.eye - world).norm();
            if n.dot(view) < 0.0 {
                n = -n;
            }
            let col = shader(world, n, view);
            t.depth[i] = iz;
            t.color[i] = [col[0], col[1], col[2], 1.0];
            t.id[i] = id;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::mesh::{Mesh, PRIM_SPHERE};

    #[test]
    fn canvas_camera_maps_plane_one_to_one() {
        let cam = Camera::canvas(800.0, 600.0, 1.0);
        let (x, y, _) = cam.project(v3(100.0, 50.0, 0.0));
        assert!((x - 100.0).abs() < 1e-2 && (y - 50.0).abs() < 1e-2, "{x} {y}");
    }

    #[test]
    fn sphere_renders_and_is_lit_from_top_left() {
        let (w, h) = (200usize, 200usize);
        let obj = Obj {
            id: 7,
            parts: Arc::new(vec![Part { mesh: Arc::new(Mesh::primitive(PRIM_SPHERE)), m: M3::IDENTITY, off: V3::default(), color: None }]),
            pos: v3(100.0, 100.0, -60.0),
            rot: M3::IDENTITY,
            scale: v3(120.0, 120.0, 120.0),
            color: [0.8, 0.2, 0.2],
            finish: FINISH_GLOSS,
            group: 0,
        };
        let mut color = vec![[0.0f32; 4]; w * h];
        let mut depth = vec![0.0f32; w * h];
        let mut id = vec![0u32; w * h];
        let mut t = Target { w, h, y0: 0, color: &mut color, depth: &mut depth, id: &mut id };
        render(&[obj], &Camera::canvas(w as f32, h as f32, 1.0), &Light::default(), None, &mut t);
        assert_eq!(id[100 * w + 100], 7);
        assert_eq!(id[5 * w + 5], 0);
        let lum = |x: usize, y: usize| color[y * w + x][0] + color[y * w + x][1] + color[y * w + x][2];
        assert!(lum(75, 75) > lum(125, 125));
    }
}
