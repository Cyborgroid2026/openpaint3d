# OpenPaint 3D

Ubuntu용 Paint 3D 스타일 그림판입니다. 화면 구성과 기능은 Paint 3D를 참고했고, 코드는 C#(Avalonia)과 Rust로 새로 작성했습니다.
Microsoft의 코드, 아이콘, 리소스는 포함되어 있지 않으며 Microsoft와 관련이 없습니다.

## 설치

[Releases](../../releases)에서 `openpaint3d_<버전>_amd64.deb`를 받아 설치합니다.

```
sudo apt install ./openpaint3d_0.3.0_amd64.deb
```

## 구성

| 폴더 | 내용 |
|---|---|
| `engine/` | Rust 엔진(cdylib, C ABI `op3d_*`): 캔버스, 브러시와 재질, 3D 개체와 소프트웨어 래스터라이저, 효과, 실행 취소, `.op3d` 프로젝트 형식 |
| `app/` | C# .NET 10 + Avalonia 11 UI(XAML 없이 코드로 구성) |
| `uitest/` | Avalonia.Headless로 실제 창을 띄워 클릭·드래그를 흉내 내는 UI 테스트 |
| `packaging/` | `build-deb.sh`: 엔진 테스트와 UI 테스트를 통과하면 `dist/`에 .deb를 만듭니다 |

## 빌드

필요한 것: Rust 1.75 이상, .NET 10 SDK, `dpkg-deb`

```
bash packaging/build-deb.sh
```

## 지원하지 않는 기능

FBX·비디오 내보내기, 3D 표면에 그리기·스티커, 매직 선택, 혼합 현실, 다중 선택·그룹, 온라인 Remix 3D 라이브러리
