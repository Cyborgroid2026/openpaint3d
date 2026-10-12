use openpaint3d_engine::*;
use std::time::Instant;
fn main() {
    unsafe {
        let c = op3d_new(1920, 1080);
        let t = Instant::now();
        op3d_display(c, 1);
        println!("compose empty full: {:?}", t.elapsed());
        for k in 0..6u32 {
            op3d_obj_begin(c);
            let part = [0.0f32, 0.0, 0.0, 1.0, 1.0, 1.0, 0.0, 0.0, 0.0];
            op3d_obj_part(c, k, part.as_ptr(), 0);
            let tr = [300.0 + 250.0 * k as f32, 500.0, -150.0, 220.0, 220.0, 220.0, 20.0, 30.0, 0.0];
            op3d_obj_commit(c, tr.as_ptr(), 0x3366CCFF, k % 4, 7);
        }
        for ss in [1, 2] {
            let t = Instant::now();
            op3d_display(c, ss);
            println!("compose with 6 objects ss={ss}: {:?}", t.elapsed());
        }
        let t = Instant::now();
        op3d_stroke_begin(c, 0, 0xFF0000FF, 20.0, 1.0, 1, 100.0, 100.0);
        for i in 0..200 { op3d_stroke_to(c, 100.0 + i as f32 * 5.0, 100.0 + (i as f32 * 0.1).sin() * 80.0); }
        op3d_stroke_end(c);
        println!("glossy stroke 1000px: {:?}", t.elapsed());
        let t = Instant::now();
        op3d_display(c, 2);
        println!("recompose after stroke: {:?}", t.elapsed());
        let mut out = vec![0u8; 1280 * 800 * 4];
        let t = Instant::now();
        op3d_render_3d(c, out.as_mut_ptr(), 1280, 800, 30.0, 20.0, 1.0, 1);
        println!("3D view 1280x800: {:?}", t.elapsed());
    }
}
