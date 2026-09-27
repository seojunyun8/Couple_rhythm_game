# 리허설용 이성 매칭 참가자 60명씩 등록

`seed_test_match_pool_cloud_shell.py`는 Firebase 프로젝트의 기존 참가자와 인형 재고를 건드리지 않고, 가상 참가자를 남 60명·여 60명 추가합니다. `Participants`와 `ParticipantKeys`를 함께 만들고 `GameState/stats.totalRegistrations`를 1씩 증가시킵니다. 기존 ID가 이미 있으면 덮어쓰지 않습니다. 같은 명령을 다시 실행해도 이미 등록된 테스트 참가자의 `isPicked` 값은 초기화하지 않습니다.

Firebase 콘솔에서 대상 프로젝트 `ludens-booth26-2`를 선택하고 Cloud Shell을 연 다음 실행합니다. 브라우저의 Firebase 로그인과 Cloud Shell의 Google 계정 인증은 별개입니다.

```bash
gcloud config set project ludens-booth26-2
curl -fsSLo ~/seed_test_match_pool_cloud_shell.py https://raw.githubusercontent.com/Jihwan2765/love_catcher/main/admin_tools/seed_test_match_pool_cloud_shell.py
python3 ~/seed_test_match_pool_cloud_shell.py --project-id ludens-booth26-2
python3 ~/seed_test_match_pool_cloud_shell.py --project-id ludens-booth26-2 --apply
```

마지막 줄에서 `120/120 처리`와 `신규 120`을 확인합니다. 이미 일부가 등록되어 있으면 `신규 + 기존 + 응답유실 복구 = 120`이 정상입니다. 중간에 오류가 나면 원인을 해결한 뒤 같은 명령을 다시 실행합니다. 비밀번호, API 키, 액세스 토큰을 명령에 넣거나 채팅으로 보내지 않습니다.

이 프로필은 실제 인스타그램 계정이 아니며 `isTestData=true`와 `booth_test_20260927_` 접두어로 구분됩니다. 실전 운영 전에 테스트 프로필과 테스트 결과를 별도 정리해야 합니다. 게임에서 테스트 ID가 뽑히면 `isPicked=true`가 되어 재실행으로는 후보 풀에 돌아오지 않습니다. 이 도구는 상품 수량과 매출을 변경하지 않습니다.
