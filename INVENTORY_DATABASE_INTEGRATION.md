# 커플 리듬게임 공유 재고 연동 (2026-09-26)

**운영 빌드 전 실게임·Unity 검증 필요.** 2026-09-27 운영 계정의 스태프 클레임과 게시된 보안 규칙으로 읽기 권한을 확인했습니다. 상품 지급 쓰기·동시성·Windows 빌드는 아직 검증하지 않았습니다.

- 커플 리듬은 인스타 ID를 저장하지 않습니다.
- 결제 확인 버튼은 기존 1,000원 기록을 `GameRounds/rhythm_{roundId}` 영수증과 함께 한 번만 저장합니다. 같은 버튼 연타와 응답 유실 재시도는 같은 회차 ID를 사용합니다.
- 곡 완료 성적은 `RhythmLeaderboard/{roundId}`에 저장해 중복 행을 막습니다. 통신이 끊겼으면 결과 화면에서 운영진이 `Ctrl+Alt+S`로 같은 회차의 점수 저장을 재시도할 수 있습니다.
- 정확도 90~99.9%는 일반 `totalDolls`, 100%는 `totalLegendaryDolls` 재고를 사용합니다.
- 결과 화면에서 `boothAdmin` 운영진이 `Ctrl+Alt+P`를 누르면 현재 재고를 확인하고 `InventoryChanges/rhythm_{roundId}` 영수증, 공유 재고 차감, 성공 횟수 증가를 단일 Firestore `commit`으로 확정합니다. **재고 확정 후 실물 상품을 건네세요.**
- 품절·통신 오류로 확정할 수 없으면 자동 지급을 보류하고 운영진이 회차 ID로 수기 대조합니다. 하위 상품 대체는 하지 않습니다.
- 재고 수량과 기본 플레이/매출 카운터는 `GameState/stats`에 운영진이 실제 값으로 초기화합니다. 인형 100개를 코드가 임의로 생성하지 않습니다.
- REST 요청은 Firebase 이메일 로그인으로 받은 ID 토큰을 사용합니다. 운영 `firestore.rules` 게시 후 익명 읽기 거부와 스태프 읽기 허용을 확인했습니다.
- 스태프 로그인 전에는 결제 확인 기록 버튼이 Firestore 요청을 시작하지 않으며, 로그인 안내를 표시합니다.
- Firebase 프로젝트 ID와 Web API Key는 `Assets/Resources/FirebaseConfig.json`에 포함되어 있습니다. Windows 빌드 시 두 값을 실행 파일 옆 `.env`로 자동 배치하며, 프로젝트 루트의 Git 제외 `.env`나 환경변수로 재정의할 수 있습니다. 설정 누락·프로젝트 불일치 시 빌드를 중단합니다. `.exe`, `_Data` 폴더, 생성된 `.env`를 함께 배포하고 비밀번호는 넣지 않습니다.
- 결과 화면에서 `Ctrl+Alt+P`는 실제 상품 당첨 회차에만 적용되며, 한 번 확정한 회차를 다시 눌러도 추가 차감하지 않습니다.

로컬 JSON 모의 검사는 `dotnet run --project Tests/FirestoreRest/FirestoreRestChecks.csproj`로 실행합니다. 이 검사는 실제 Firebase 접속을 대신하지 않습니다.

## 운영 전에 할 일

1. 별도 Firebase 테스트 프로젝트에서 이메일/비밀번호 로그인, 스태프 계정, `boothStaff` 클레임을 준비합니다. `admin_tools/grant_staff_claim.py`는 기본 미리보기이며 `--apply`에서만 변경합니다.
2. `Assets/Resources/FirebaseConfig.json`의 프로젝트 ID와 Web API Key가 같은 Firebase 프로젝트인지 확인합니다. 각 기기의 `.env`는 로컬 설정을 재정의할 때만 사용하며, 프로젝트 ID는 JSON 설정과 같아야 합니다. 비밀번호와 관리용 ADC 파일은 저장소에 넣지 않습니다.
3. `GameState/stats` 6개 정수 필드를 실제 재고로 만듭니다.
4. 테스트 프로젝트에 `firestore.rules`와 `firestore.indexes.json`을 배포하고 인증 없는 조회·쓰기·쿼리·commit이 거부되는지 확인합니다.
5. Windows 빌드에서 90%/100%, 재고 1개에 동시 2건, 버튼 연타, 응답 유실, Wi-Fi 끊김을 확인합니다. 기록과 실제 지급 수량이 같아야 합니다.
6. 운영 프로젝트의 규칙 게시와 익명 조회 차단은 확인했습니다. 실제 상품 지급 쓰기·실게임 흐름은 아직 확인해야 합니다.

참고: `admin_tools`의 관리용 REST 요청은 Google ADC를 사용합니다. 위험한 시드·시뮬레이션의 운영 DB 직접 쓰기는 차단했습니다.
