# OpenPaint 3D

Ubuntu용 Paint 3D 스타일 그림판입니다. 화면 구성과 기능은 Paint 3D를 참고했고, 코드는 C#(Avalonia)과 Rust로 새로 작성했습니다.
Microsoft의 코드, 아이콘, 리소스는 포함되어 있지 않으며 Microsoft와 관련이 없습니다.

## 설치

[Releases](../../releases)에서 `openpaint3d_<버전>_amd64.deb`를 받아 설치합니다.

```
sudo apt install ./openpaint3d_0.4.1_amd64.deb
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

## 단축키

Paint 3D와 같은 키를 씁니다. 한글 입력 상태에서도 동작합니다.

| 키 | 기능 |
|---|---|
| Ctrl+N / O / S / Shift+S | 새로 만들기 / 열기 / 저장 / 다른 이름으로 저장 |
| Ctrl+P | 인쇄 (2D 인쇄·PDF로 저장, 3D 인쇄) |
| Ctrl+Z / Y | 실행 취소 / 다시 실행 |
| Ctrl+C / X / V | 복사 / 잘라내기 / 붙여넣기 (2D 선택 영역과 3D 개체) |
| Ctrl+A | 3D 개체 모두 선택 (개체가 없으면 캔버스 전체 선택) |
| Ctrl+D | 선택한 것 복제 |
| Ctrl+G / Shift+G | 그룹 / 그룹 해제 |
| Ctrl+B / I / U | 텍스트 굵게 / 기울임꼴 / 밑줄 |
| Ctrl + `+` / `-` | 브러시·선 두께, 글꼴 크기 |
| Ctrl+PgUp / PgDn, 마우스 휠 | 확대 / 축소 (Shift+휠: 가로 스크롤, 휠 버튼 끌기: 이동) |
| Ctrl+0 | 화면에 맞추기 |
| Ctrl+3 | 3D 보기 켜기/끄기 |
| Ctrl+Shift+E | 3D 보기 원근감 켜기/끄기 |
| Ctrl+Shift+X | 자르기 |
| Ctrl+Shift+C | 완성된 그림을 클립보드로 복사 |
| Ctrl+E | 캔버스 |
| I / M | 스포이드 / 오른쪽 패널 숨기기·표시 |
| 방향키 (Shift) | 선택한 것을 1픽셀(10픽셀)씩 이동 |
| Delete, Enter, Esc | 삭제, 확정, 취소·선택 해제 |

## 지원하지 않는 기능

FBX·비디오 내보내기, 3D 표면에 그리기·스티커, 매직 선택, 혼합 현실, 온라인 Remix 3D 라이브러리
