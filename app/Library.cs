namespace OpenPaint3D;

/// <summary>
/// Built-in 3D models, composed from engine primitives inside a unit box.
/// Object space: x right, y down (so "up" is -y), z away from the viewer.
/// </summary>
public static class Library
{
    public const int Cube = 0, Sphere = 1, Cylinder = 2, Cone = 3, Pyramid = 4, Capsule = 5, Torus = 6, Dome = 7;

    public sealed record Model(string Name, string Category, string Color, int Finish, ModelPart[] Parts, string Keywords = "");

    public static readonly string[] BasicNames = { "정육면체", "구", "반구", "원뿔", "피라미드", "원기둥", "캡슐", "도넛" };
    public static readonly int[] BasicPrims = { Cube, Sphere, Dome, Cone, Pyramid, Cylinder, Capsule, Torus };

    public static readonly string[] Categories = { "동물", "우주", "공룡", "비닐 토이", "사실적", "꽃·식물", "멋진 자동차", "생물학", "점성술" };

    private static ModelPart P(int prim, double x, double y, double z, double sx, double sy, double sz, string? c = null,
        double rx = 0, double ry = 0, double rz = 0) => new(prim, x, y, z, sx, sy, sz, rx, ry, rz, c);

    private const string Skin = "#F2C4A0", Dark = "#2B2B2B", White = "#FAFAFA";

    // ------------------------------------------------------- 3D shapes panel models

    public static readonly Model Man = new("남자", "사람", "#3F6FD8", 0, new[]
    {
        P(Capsule, 0, 0.02, 0, 0.36, 0.5, 0.26),                       // torso (object colour)
        P(Sphere, 0, -0.34, 0, 0.27, 0.27, 0.27, Skin),                // head
        P(Dome, 0, -0.39, 0, 0.29, 0.22, 0.29, "#4A2F1E"),             // hair
        P(Capsule, -0.085, 0.34, 0, 0.13, 0.36, 0.13, "#2F3D57"),      // legs
        P(Capsule, 0.085, 0.34, 0, 0.13, 0.36, 0.13, "#2F3D57"),
        P(Capsule, -0.23, 0.04, 0, 0.1, 0.38, 0.1, null, 0, 0, 14),   // arms
        P(Capsule, 0.23, 0.04, 0, 0.1, 0.38, 0.1, null, 0, 0, -14),
    });

    public static readonly Model Woman = new("여자", "사람", "#E2577E", 0, new[]
    {
        P(Cone, 0, 0.12, 0, 0.46, 0.5, 0.4),                            // dress
        P(Capsule, 0, -0.1, 0, 0.22, 0.3, 0.2),
        P(Sphere, 0, -0.34, 0, 0.26, 0.26, 0.26, Skin),
        P(Capsule, 0, -0.26, 0.04, 0.32, 0.42, 0.26, "#7A3E1D"),       // long hair behind
        P(Capsule, -0.07, 0.42, 0, 0.08, 0.2, 0.08, Skin),
        P(Capsule, 0.07, 0.42, 0, 0.08, 0.2, 0.08, Skin),
        P(Capsule, -0.17, -0.02, 0, 0.08, 0.32, 0.08, Skin, 0, 0, 18),
        P(Capsule, 0.17, -0.02, 0, 0.08, 0.32, 0.08, Skin, 0, 0, -18),
    });

    public static readonly Model Dog = new("강아지", "동물", "#B07A45", 0, new[]
    {
        P(Capsule, 0.05, 0.05, 0, 0.32, 0.62, 0.32, null, 0, 0, 90),   // body
        P(Sphere, -0.3, -0.18, 0, 0.32, 0.3, 0.3),                       // head
        P(Sphere, -0.45, -0.13, 0, 0.16, 0.12, 0.14, "#E8D2B5"),         // snout
        P(Sphere, -0.53, -0.16, 0, 0.06, 0.05, 0.05, Dark),              // nose
        P(Sphere, -0.36, -0.25, -0.1, 0.05, 0.05, 0.05, Dark),           // eye
        P(Sphere, -0.24, -0.1, -0.15, 0.1, 0.22, 0.06, "#6B4423", 0, 0, -20), // ear
        P(Cylinder, -0.15, 0.3, -0.08, 0.08, 0.24, 0.08),               // legs
        P(Cylinder, -0.15, 0.3, 0.08, 0.08, 0.24, 0.08),
        P(Cylinder, 0.24, 0.3, -0.08, 0.08, 0.24, 0.08),
        P(Cylinder, 0.24, 0.3, 0.08, 0.08, 0.24, 0.08),
        P(Cone, 0.42, -0.08, 0, 0.07, 0.24, 0.07, null, 0, 0, 40),      // tail
    }, "개 dog 강아지");

    public static readonly Model Cat = new("고양이", "동물", "#9EA3AA", 0, new[]
    {
        P(Capsule, 0.06, 0.1, 0, 0.3, 0.58, 0.3, null, 0, 0, 90),
        P(Sphere, -0.27, -0.12, 0, 0.3, 0.27, 0.27),
        P(Pyramid, -0.36, -0.3, 0, 0.1, 0.14, 0.06),
        P(Pyramid, -0.18, -0.3, 0, 0.1, 0.14, 0.06),
        P(Sphere, -0.33, -0.15, -0.13, 0.05, 0.06, 0.03, "#7CB342"),
        P(Sphere, -0.2, -0.15, -0.13, 0.05, 0.06, 0.03, "#7CB342"),
        P(Sphere, -0.27, -0.06, -0.14, 0.04, 0.03, 0.03, "#F48FB1"),
        P(Cylinder, -0.12, 0.33, -0.08, 0.07, 0.2, 0.07),
        P(Cylinder, -0.12, 0.33, 0.08, 0.07, 0.2, 0.07),
        P(Cylinder, 0.26, 0.33, -0.08, 0.07, 0.2, 0.07),
        P(Cylinder, 0.26, 0.33, 0.08, 0.07, 0.2, 0.07),
        P(Capsule, 0.42, -0.12, 0, 0.06, 0.42, 0.06, null, 0, 0, -25),
    }, "cat 고양이 냥이");

    public static readonly Model Fish = new("물고기", "동물", "#FF8A3D", 1, new[]
    {
        P(Sphere, -0.08, 0, 0, 0.66, 0.42, 0.24),
        P(Pyramid, 0.36, 0, 0, 0.26, 0.4, 0.06, null, 0, 0, 90),
        P(Pyramid, -0.04, -0.24, 0, 0.22, 0.14, 0.04),
        P(Sphere, -0.27, -0.05, -0.1, 0.08, 0.08, 0.05, White),
        P(Sphere, -0.28, -0.05, -0.13, 0.04, 0.04, 0.03, Dark),
    }, "fish 물고기 생선");

    public static readonly Model[] People = { Man, Woman, Dog, Cat, Fish };

    // ---------------------------------------------------------------- 3D library

    public static readonly Model[] All =
    {
        Dog, Cat, Fish,
        new("펭귄", "동물", "#263238", 1, new[]
        {
            P(Capsule, 0, 0.05, 0, 0.5, 0.82, 0.42),
            P(Sphere, 0, 0.12, -0.12, 0.36, 0.56, 0.22, White),
            P(Sphere, -0.08, -0.22, -0.16, 0.07, 0.07, 0.05, White),
            P(Sphere, 0.08, -0.22, -0.16, 0.07, 0.07, 0.05, White),
            P(Sphere, -0.08, -0.22, -0.19, 0.035, 0.035, 0.03, Dark),
            P(Sphere, 0.08, -0.22, -0.19, 0.035, 0.035, 0.03, Dark),
            P(Cone, 0, -0.14, -0.22, 0.09, 0.14, 0.09, "#FFA000", 90),
            P(Sphere, -0.12, 0.45, -0.08, 0.16, 0.06, 0.2, "#FFA000"),
            P(Sphere, 0.12, 0.45, -0.08, 0.16, 0.06, 0.2, "#FFA000"),
            P(Capsule, -0.27, 0.05, 0, 0.08, 0.42, 0.14, null, 0, 0, 12),
            P(Capsule, 0.27, 0.05, 0, 0.08, 0.42, 0.14, null, 0, 0, -12),
        }, "penguin 펭귄 새"),
        new("로켓", "우주", "#ECEFF1", 2, new[]
        {
            P(Cylinder, 0, 0.08, 0, 0.3, 0.56, 0.3),
            P(Cone, 0, -0.32, 0, 0.3, 0.26, 0.3, "#E53935"),
            P(Sphere, 0, -0.04, -0.14, 0.14, 0.14, 0.06, "#4FC3F7"),
            P(Pyramid, -0.2, 0.3, 0, 0.16, 0.3, 0.04, "#E53935", 0, 0, -20),
            P(Pyramid, 0.2, 0.3, 0, 0.16, 0.3, 0.04, "#E53935", 0, 0, 20),
            P(Cone, 0, 0.44, 0, 0.18, 0.14, 0.18, "#FFB300", 180),
        }, "rocket 로켓 우주선"),
        new("토성", "우주", "#E3C08D", 0, new[]
        {
            P(Sphere, 0, 0, 0, 0.56, 0.56, 0.56),
            P(Torus, 0, 0, 0, 1.0, 0.25, 1.0, "#C9A46B", 18, 0, 12),
        }, "saturn 토성 행성 planet"),
        new("UFO", "우주", "#B0BEC5", 3, new[]
        {
            P(Sphere, 0, 0.08, 0, 1.0, 0.24, 1.0),
            P(Dome, 0, -0.06, 0, 0.46, 0.4, 0.46, "#80DEEA"),
            P(Sphere, -0.3, 0.16, -0.3, 0.07, 0.07, 0.07, "#FFEB3B"),
            P(Sphere, 0.3, 0.16, -0.3, 0.07, 0.07, 0.07, "#FFEB3B"),
            P(Sphere, 0, 0.18, -0.42, 0.07, 0.07, 0.07, "#FFEB3B"),
        }, "ufo 비행접시 외계인"),
        new("공룡", "공룡", "#66BB6A", 0, new[]
        {
            P(Capsule, 0.02, 0.06, 0, 0.36, 0.62, 0.34, null, 0, 0, 90),
            P(Capsule, -0.25, -0.18, 0, 0.14, 0.36, 0.14, null, 0, 0, 35),
            P(Sphere, -0.36, -0.36, 0, 0.22, 0.16, 0.16),
            P(Sphere, -0.42, -0.4, -0.07, 0.04, 0.04, 0.03, Dark),
            P(Cone, 0.4, 0.1, 0, 0.16, 0.42, 0.16, null, 0, 0, 100),
            P(Cylinder, -0.12, 0.32, -0.09, 0.1, 0.24, 0.1),
            P(Cylinder, -0.12, 0.32, 0.09, 0.1, 0.24, 0.1),
            P(Cylinder, 0.18, 0.32, -0.09, 0.1, 0.24, 0.1),
            P(Cylinder, 0.18, 0.32, 0.09, 0.1, 0.24, 0.1),
            P(Pyramid, -0.08, -0.15, 0, 0.1, 0.12, 0.04, "#FF7043"),
            P(Pyramid, 0.06, -0.15, 0, 0.1, 0.12, 0.04, "#FF7043"),
            P(Pyramid, 0.2, -0.12, 0, 0.08, 0.1, 0.04, "#FF7043"),
        }, "dinosaur 공룡 dino"),
        new("트리케라톱스", "공룡", "#8D6E63", 0, new[]
        {
            P(Sphere, 0.08, 0.04, 0, 0.66, 0.42, 0.4),
            P(Sphere, -0.28, -0.02, 0, 0.26, 0.24, 0.24),
            P(Cylinder, -0.2, -0.08, 0, 0.36, 0.36, 0.06, "#BCAAA4", 0, 90, 0),
            P(Cone, -0.36, -0.2, -0.06, 0.05, 0.2, 0.05, White, 0, 0, -50),
            P(Cone, -0.36, -0.2, 0.06, 0.05, 0.2, 0.05, White, 0, 0, -50),
            P(Cone, -0.44, -0.04, 0, 0.05, 0.12, 0.05, White, 0, 0, -70),
            P(Cylinder, -0.1, 0.3, -0.1, 0.1, 0.22, 0.1),
            P(Cylinder, -0.1, 0.3, 0.1, 0.1, 0.22, 0.1),
            P(Cylinder, 0.24, 0.3, -0.1, 0.1, 0.22, 0.1),
            P(Cylinder, 0.24, 0.3, 0.1, 0.1, 0.22, 0.1),
            P(Cone, 0.44, 0.04, 0, 0.1, 0.22, 0.1, null, 0, 0, 100),
        }, "triceratops 트리케라톱스 공룡"),
        new("곰 인형", "비닐 토이", "#C68B59", 0, new[]
        {
            P(Sphere, 0, 0.18, 0, 0.5, 0.56, 0.44),
            P(Sphere, 0, -0.24, 0, 0.42, 0.4, 0.38),
            P(Sphere, -0.16, -0.42, 0, 0.14, 0.14, 0.1),
            P(Sphere, 0.16, -0.42, 0, 0.14, 0.14, 0.1),
            P(Sphere, 0, -0.18, -0.16, 0.16, 0.12, 0.1, "#E8C9A8"),
            P(Sphere, 0, -0.21, -0.21, 0.06, 0.05, 0.04, Dark),
            P(Sphere, -0.08, -0.29, -0.17, 0.045, 0.045, 0.03, Dark),
            P(Sphere, 0.08, -0.29, -0.17, 0.045, 0.045, 0.03, Dark),
            P(Capsule, -0.27, 0.12, 0, 0.13, 0.32, 0.13, null, 0, 0, 30),
            P(Capsule, 0.27, 0.12, 0, 0.13, 0.32, 0.13, null, 0, 0, -30),
            P(Sphere, -0.14, 0.44, -0.04, 0.17, 0.12, 0.2),
            P(Sphere, 0.14, 0.44, -0.04, 0.17, 0.12, 0.2),
        }, "bear 곰 인형 teddy"),
        new("로봇", "비닐 토이", "#90A4AE", 2, new[]
        {
            P(Cube, 0, 0.1, 0, 0.44, 0.4, 0.3),
            P(Cube, 0, -0.26, 0, 0.32, 0.26, 0.26),
            P(Sphere, -0.07, -0.27, -0.13, 0.07, 0.07, 0.03, "#29B6F6"),
            P(Sphere, 0.07, -0.27, -0.13, 0.07, 0.07, 0.03, "#29B6F6"),
            P(Cylinder, 0, -0.44, 0, 0.03, 0.1, 0.03, Dark),
            P(Sphere, 0, -0.5, 0, 0.06, 0.06, 0.06, "#E53935"),
            P(Cylinder, -0.29, 0.1, 0, 0.09, 0.36, 0.09),
            P(Cylinder, 0.29, 0.1, 0, 0.09, 0.36, 0.09),
            P(Cube, -0.1, 0.4, 0, 0.12, 0.22, 0.14),
            P(Cube, 0.1, 0.4, 0, 0.12, 0.22, 0.14),
            P(Cube, 0, 0.08, -0.16, 0.2, 0.12, 0.03, "#FFCA28"),
        }, "robot 로봇"),
        new("머그컵", "사실적", "#FAFAFA", 1, new[]
        {
            P(Cylinder, -0.08, 0, 0, 0.62, 0.8, 0.62),
            P(Cylinder, -0.08, -0.38, 0, 0.54, 0.05, 0.54, "#4E342E"),
            P(Torus, 0.26, 0, 0, 0.4, 0.4, 0.4, null, 90, 0, 0),
        }, "mug cup 컵 머그"),
        new("의자", "사실적", "#A1887F", 0, new[]
        {
            P(Cube, 0, 0.05, 0, 0.6, 0.08, 0.6),
            P(Cube, 0, -0.25, 0.27, 0.6, 0.55, 0.06),
            P(Cylinder, -0.25, 0.28, -0.25, 0.06, 0.45, 0.06),
            P(Cylinder, 0.25, 0.28, -0.25, 0.06, 0.45, 0.06),
            P(Cylinder, -0.25, 0.28, 0.25, 0.06, 0.45, 0.06),
            P(Cylinder, 0.25, 0.28, 0.25, 0.06, 0.45, 0.06),
        }, "chair 의자 가구"),
        new("나무", "꽃·식물", "#43A047", 0, new[]
        {
            P(Cylinder, 0, 0.3, 0, 0.14, 0.42, 0.14, "#795548"),
            P(Sphere, 0, -0.15, 0, 0.6, 0.55, 0.6),
            P(Sphere, -0.2, 0, -0.05, 0.4, 0.38, 0.4),
            P(Sphere, 0.22, -0.02, 0.02, 0.38, 0.36, 0.38),
        }, "tree 나무 숲"),
        new("꽃", "꽃·식물", "#F06292", 1, new[]
        {
            P(Cylinder, 0, 0.18, 0, 0.04, 0.62, 0.04, "#43A047"),
            P(Sphere, 0.1, 0.25, 0, 0.2, 0.08, 0.12, "#66BB6A", 0, 0, -30),
            P(Sphere, 0, -0.25, -0.03, 0.14, 0.14, 0.1, "#FFD54F"),
            P(Sphere, 0, -0.4, 0, 0.16, 0.16, 0.08),
            P(Sphere, 0.14, -0.29, 0, 0.16, 0.16, 0.08),
            P(Sphere, 0.09, -0.12, 0, 0.16, 0.16, 0.08),
            P(Sphere, -0.09, -0.12, 0, 0.16, 0.16, 0.08),
            P(Sphere, -0.14, -0.29, 0, 0.16, 0.16, 0.08),
        }, "flower 꽃 식물"),
        new("선인장", "꽃·식물", "#2E7D32", 0, new[]
        {
            P(Capsule, 0, -0.05, 0, 0.26, 0.72, 0.26),
            P(Capsule, -0.2, -0.08, 0, 0.14, 0.3, 0.14),
            P(Capsule, -0.13, 0.02, 0, 0.16, 0.08, 0.1, null, 0, 0, 90),
            P(Capsule, 0.2, -0.16, 0, 0.14, 0.26, 0.14),
            P(Capsule, 0.13, -0.08, 0, 0.16, 0.08, 0.1, null, 0, 0, 90),
            P(Cylinder, 0, 0.38, 0, 0.42, 0.24, 0.42, "#D84315"),
        }, "cactus 선인장 식물"),
        new("자동차", "멋진 자동차", "#E53935", 1, new[]
        {
            P(Cube, 0, 0.06, 0, 0.94, 0.24, 0.48),
            P(Cube, 0.04, -0.14, 0, 0.5, 0.2, 0.44, null),
            P(Cube, 0.04, -0.14, -0.225, 0.42, 0.14, 0.01, "#90CAF9"),
            P(Cylinder, -0.3, 0.2, -0.24, 0.22, 0.08, 0.22, Dark, 90),
            P(Cylinder, 0.3, 0.2, -0.24, 0.22, 0.08, 0.22, Dark, 90),
            P(Cylinder, -0.3, 0.2, 0.24, 0.22, 0.08, 0.22, Dark, 90),
            P(Cylinder, 0.3, 0.2, 0.24, 0.22, 0.08, 0.22, Dark, 90),
            P(Sphere, -0.47, 0.04, -0.15, 0.04, 0.06, 0.08, "#FFF59D"),
            P(Sphere, -0.47, 0.04, 0.15, 0.04, 0.06, 0.08, "#FFF59D"),
        }, "car 자동차 차"),
        new("스포츠카", "멋진 자동차", "#FFCA28", 3, new[]
        {
            P(Capsule, 0, 0.08, 0, 0.3, 0.98, 0.5, null, 0, 0, 90),
            P(Sphere, 0.06, -0.06, 0, 0.42, 0.22, 0.4, "#37474F"),
            P(Cube, 0.44, -0.06, 0, 0.06, 0.04, 0.5, Dark),
            P(Cylinder, -0.3, 0.2, -0.25, 0.2, 0.08, 0.2, Dark, 90),
            P(Cylinder, 0.3, 0.2, -0.25, 0.2, 0.08, 0.2, Dark, 90),
            P(Cylinder, -0.3, 0.2, 0.25, 0.2, 0.08, 0.2, Dark, 90),
            P(Cylinder, 0.3, 0.2, 0.25, 0.2, 0.08, 0.2, Dark, 90),
        }, "sports car 스포츠카 자동차"),
        new("세포", "생물학", "#81D4FA", 1, new[]
        {
            P(Sphere, 0, 0, 0, 1.0, 0.8, 0.5),
            P(Sphere, 0.05, 0, -0.18, 0.34, 0.3, 0.2, "#7E57C2"),
            P(Sphere, -0.25, 0.15, -0.18, 0.12, 0.08, 0.06, "#FF8A65"),
            P(Sphere, 0.3, -0.15, -0.17, 0.14, 0.07, 0.06, "#FF8A65"),
            P(Sphere, -0.2, -0.2, -0.17, 0.08, 0.08, 0.06, "#AED581"),
        }, "cell 세포 생물"),
        new("DNA", "생물학", "#42A5F5", 1, Dna(), "dna 유전자 나선"),
        new("별", "점성술", "#FFD54F", 3, StarParts(), "star 별"),
        new("해", "점성술", "#FFB300", 1, SunParts(), "sun 해 태양"),
        new("수정 구슬", "점성술", "#B39DDB", 3, new[]
        {
            P(Sphere, 0, -0.1, 0, 0.72, 0.72, 0.72),
            P(Cylinder, 0, 0.36, 0, 0.6, 0.16, 0.6, "#6D4C41"),
            P(Sphere, -0.12, -0.25, -0.3, 0.1, 0.1, 0.05, White),
        }, "crystal ball 수정 구슬 점"),
    };

    private static ModelPart[] Dna()
    {
        var parts = new List<ModelPart>();
        for (int i = 0; i < 10; i++)
        {
            double y = -0.45 + i * 0.1, a = i * 36.0, r = a * Math.PI / 180;
            double x = Math.Cos(r) * 0.25, z = Math.Sin(r) * 0.25;
            parts.Add(P(Sphere, x, y, z, 0.1, 0.1, 0.1, "#EF5350"));
            parts.Add(P(Sphere, -x, y, -z, 0.1, 0.1, 0.1, "#FFCA28"));
            parts.Add(P(Cylinder, 0, y, 0, 0.03, 0.5, 0.03, null, 0, -a, 90));
        }
        return parts.ToArray();
    }

    private static ModelPart[] StarParts()
    {
        // five pyramids around a flattened centre
        var parts = new List<ModelPart> { P(Sphere, 0, 0, 0, 0.36, 0.36, 0.2) };
        for (int i = 0; i < 5; i++)
        {
            double a = -90 + i * 72, r = a * Math.PI / 180;
            parts.Add(P(Pyramid, Math.Cos(r) * 0.3, Math.Sin(r) * 0.3, 0, 0.2, 0.36, 0.14, null, 0, 0, a + 90));
        }
        return parts.ToArray();
    }

    private static ModelPart[] SunParts()
    {
        var parts = new List<ModelPart> { P(Sphere, 0, 0, 0, 0.56, 0.56, 0.56) };
        for (int i = 0; i < 8; i++)
        {
            double a = i * 45, r = a * Math.PI / 180;
            parts.Add(P(Cone, Math.Cos(r) * 0.38, Math.Sin(r) * 0.38, 0, 0.12, 0.2, 0.12, "#FFCA28", 0, 0, a + 90));
        }
        return parts.ToArray();
    }

    public static IEnumerable<Model> Search(string? category, string query)
    {
        var q = query.Trim();
        return All.Where(m => (string.IsNullOrEmpty(category) || m.Category == category) &&
                              (q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               m.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               m.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }
}
