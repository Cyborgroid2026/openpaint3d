//! Small vector / matrix helpers.
//!
//! World space: x right, y down, z into the screen (right-handed).
//! The canvas lies on z = 0 and 3D objects sit in front of it (z < 0).

use std::ops::{Add, Mul, Neg, Sub};

#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct V3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

pub const fn v3(x: f32, y: f32, z: f32) -> V3 {
    V3 { x, y, z }
}

impl V3 {
    pub fn dot(self, o: V3) -> f32 {
        self.x * o.x + self.y * o.y + self.z * o.z
    }
    pub fn cross(self, o: V3) -> V3 {
        v3(self.y * o.z - self.z * o.y, self.z * o.x - self.x * o.z, self.x * o.y - self.y * o.x)
    }
    pub fn len(self) -> f32 {
        self.dot(self).sqrt()
    }
    pub fn norm(self) -> V3 {
        let l = self.len();
        if l > 1e-12 {
            self * (1.0 / l)
        } else {
            v3(0.0, 0.0, -1.0)
        }
    }
    pub fn mul_elem(self, o: V3) -> V3 {
        v3(self.x * o.x, self.y * o.y, self.z * o.z)
    }
}

impl Add for V3 {
    type Output = V3;
    fn add(self, o: V3) -> V3 {
        v3(self.x + o.x, self.y + o.y, self.z + o.z)
    }
}
impl Sub for V3 {
    type Output = V3;
    fn sub(self, o: V3) -> V3 {
        v3(self.x - o.x, self.y - o.y, self.z - o.z)
    }
}
impl Mul<f32> for V3 {
    type Output = V3;
    fn mul(self, k: f32) -> V3 {
        v3(self.x * k, self.y * k, self.z * k)
    }
}
impl Neg for V3 {
    type Output = V3;
    fn neg(self) -> V3 {
        v3(-self.x, -self.y, -self.z)
    }
}

/// Row-major 3x3 matrix.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct M3(pub [[f32; 3]; 3]);

impl M3 {
    pub const IDENTITY: M3 = M3([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]);

    pub fn scale(s: V3) -> M3 {
        M3([[s.x, 0.0, 0.0], [0.0, s.y, 0.0], [0.0, 0.0, s.z]])
    }

    /// Rotation around a principal axis (0 = x, 1 = y, 2 = z), angle in degrees.
    pub fn rot_axis(axis: u32, deg: f32) -> M3 {
        let (s, c) = deg.to_radians().sin_cos();
        match axis {
            0 => M3([[1.0, 0.0, 0.0], [0.0, c, -s], [0.0, s, c]]),
            1 => M3([[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]]),
            _ => M3([[c, -s, 0.0], [s, c, 0.0], [0.0, 0.0, 1.0]]),
        }
    }

    /// Euler rotation applied x, then y, then z.
    pub fn euler(rx: f32, ry: f32, rz: f32) -> M3 {
        M3::rot_axis(2, rz).mul(&M3::rot_axis(1, ry)).mul(&M3::rot_axis(0, rx))
    }

    pub fn mul(&self, o: &M3) -> M3 {
        let mut r = [[0.0f32; 3]; 3];
        for (i, row) in r.iter_mut().enumerate() {
            for (j, cell) in row.iter_mut().enumerate() {
                *cell = (0..3).map(|k| self.0[i][k] * o.0[k][j]).sum();
            }
        }
        M3(r)
    }

    pub fn apply(&self, v: V3) -> V3 {
        let m = &self.0;
        v3(
            m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z,
            m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
            m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z,
        )
    }

    pub fn transpose(&self) -> M3 {
        let m = &self.0;
        M3([[m[0][0], m[1][0], m[2][0]], [m[0][1], m[1][1], m[2][1]], [m[0][2], m[1][2], m[2][2]]])
    }

    pub fn det(&self) -> f32 {
        let m = &self.0;
        m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
            + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0])
    }

    /// Inverse-transpose, used to transform normals.
    pub fn normal_matrix(&self) -> M3 {
        let m = &self.0;
        let d = self.det();
        if d.abs() < 1e-20 {
            return M3::IDENTITY;
        }
        let inv = 1.0 / d;
        // cofactor matrix / det == inverse transpose
        M3([
            [
                (m[1][1] * m[2][2] - m[1][2] * m[2][1]) * inv,
                -(m[1][0] * m[2][2] - m[1][2] * m[2][0]) * inv,
                (m[1][0] * m[2][1] - m[1][1] * m[2][0]) * inv,
            ],
            [
                -(m[0][1] * m[2][2] - m[0][2] * m[2][1]) * inv,
                (m[0][0] * m[2][2] - m[0][2] * m[2][0]) * inv,
                -(m[0][0] * m[2][1] - m[0][1] * m[2][0]) * inv,
            ],
            [
                (m[0][1] * m[1][2] - m[0][2] * m[1][1]) * inv,
                -(m[0][0] * m[1][2] - m[0][2] * m[1][0]) * inv,
                (m[0][0] * m[1][1] - m[0][1] * m[1][0]) * inv,
            ],
        ])
    }
}

pub fn clamp01(v: f32) -> f32 {
    v.max(0.0).min(1.0)
}

pub fn hash(x: i32, y: i32) -> f32 {
    let mut h = (x as u32).wrapping_mul(374_761_393) ^ (y as u32).wrapping_mul(668_265_263);
    h = (h ^ (h >> 13)).wrapping_mul(1_274_126_177);
    h ^= h >> 16;
    (h & 0xffff) as f32 / 65535.0
}

/// 0xRRGGBBAA -> ([r, g, b] in 0..1, alpha in 0..1)
pub fn unpack(c: u32) -> ([f32; 3], f32) {
    (
        [((c >> 24) & 255) as f32 / 255.0, ((c >> 16) & 255) as f32 / 255.0, ((c >> 8) & 255) as f32 / 255.0],
        (c & 255) as f32 / 255.0,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normal_matrix_is_inverse_transpose() {
        let m = M3::euler(20.0, 35.0, -10.0).mul(&M3::scale(v3(2.0, 0.5, 3.0)));
        let n = m.normal_matrix();
        // (M^-T)^T * M == I  =>  n^T * m == I
        let p = n.transpose().mul(&m);
        for i in 0..3 {
            for j in 0..3 {
                let e = if i == j { 1.0 } else { 0.0 };
                assert!((p.0[i][j] - e).abs() < 1e-4, "{:?}", p);
            }
        }
    }
}
