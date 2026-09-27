"""Cloud Shell에서 테스트 매칭 프로필을 성별 60명씩 안전하게 추가한다.

기존 참가자와 재고는 수정하지 않는다. 각 참가자의 ParticipantKeys,
Participants, totalRegistrations 증가를 단일 Firestore commit으로 확정한다.
기본 실행은 오프라인 미리보기이며 --apply에서만 Firebase에 접속한다.
"""

import argparse
import json
import re
import subprocess
import sys
from urllib import error, parse, request


PREFIX = "booth_test_20260927"


def profiles(count):
    for gender_code, gender, name_prefix in (("m", "남", "테스트남"), ("f", "여", "테스트여")):
        for number in range(1, count + 1):
            handle = f"{PREFIX}_{gender_code}{number:03d}"
            yield {
                "handle": handle,
                "id": "insta_" + handle,
                "name": f"{name_prefix}{number:03d}",
                "gender": gender,
            }


def gcloud(*args):
    return subprocess.check_output(["gcloud", *args], text=True, stderr=subprocess.DEVNULL).strip()


def call(project_id, token, method, suffix, payload=None):
    root = f"https://firestore.googleapis.com/v1/projects/{parse.quote(project_id, safe='')}/databases/(default)/documents"
    body = None if payload is None else json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = request.Request(root + suffix, data=body, method=method, headers={
        "Authorization": "Bearer " + token,
        "Content-Type": "application/json",
        "x-goog-user-project": project_id,
    })
    try:
        with request.urlopen(req, timeout=25) as response:
            return response.status, json.load(response)
    except error.HTTPError as exc:
        # 인증 토큰이나 요청 헤더는 출력하지 않는다.
        return exc.code, {}


def get_document(project_id, token, path):
    status, body = call(project_id, token, "GET", "/" + path)
    if status not in (200, 404):
        raise RuntimeError(f"{path} 조회 실패: HTTP {status}. 작업을 중단합니다.")
    return body if status == 200 else None


def string_field(doc, name):
    return doc.get("fields", {}).get(name, {}).get("stringValue")


def is_our_pair(person, key, profile):
    return (
        person is not None and key is not None
        and person.get("fields", {}).get("isTestData", {}).get("booleanValue") is True
        and string_field(person, "insta") == profile["handle"]
        and string_field(person, "gender") == profile["gender"]
        and string_field(key, "participantKey") == profile["id"]
    )


def write_profile(project_id, token, profile):
    doc_id = profile["id"]
    person_path = "Participants/" + doc_id
    key_path = "ParticipantKeys/" + doc_id
    person = get_document(project_id, token, person_path)
    key = get_document(project_id, token, key_path)
    if is_our_pair(person, key, profile):
        return "existing"
    if person is not None or key is not None:
        raise RuntimeError(f"{doc_id}: 같은 ID의 기존 데이터가 달라 덮어쓰지 않습니다.")

    root = f"projects/{project_id}/databases/(default)/documents/"
    fields = {
        "name": {"stringValue": profile["name"]},
        "insta": {"stringValue": profile["handle"]},
        "bio": {"stringValue": "축제 부스 리허설용 가상 프로필 · 실제 인스타 계정 아님"},
        "gender": {"stringValue": profile["gender"]},
        "isPicked": {"booleanValue": False},
        "attempts": {"integerValue": "0"},
        "isTestData": {"booleanValue": True},
    }
    payload = {"writes": [
        {"update": {"name": root + key_path, "fields": {
            "participantKey": {"stringValue": doc_id}}}, "currentDocument": {"exists": False}},
        {"update": {"name": root + person_path, "fields": fields},
         "currentDocument": {"exists": False}},
        {"transform": {"document": root + "GameState/stats", "fieldTransforms": [
            {"fieldPath": "totalRegistrations", "increment": {"integerValue": "1"}}]},
         "currentDocument": {"exists": True}},
    ]}
    status, _ = call(project_id, token, "POST", ":commit", payload)
    # 응답이 유실돼도 같은 문서를 다시 쓰지 않고 서버 상태로 확정 여부를 판단한다.
    person = get_document(project_id, token, person_path)
    key = get_document(project_id, token, key_path)
    if is_our_pair(person, key, profile):
        return "created" if status == 200 else "recovered"
    raise RuntimeError(f"{doc_id}: commit HTTP {status}, 저장 상태 불명. 작업을 중단합니다.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-id", required=True)
    parser.add_argument("--per-gender", type=int, default=60)
    parser.add_argument("--apply", action="store_true", help="실제 Firebase에 등록")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z][a-z0-9-]{4,62}", args.project_id):
        raise RuntimeError("Firebase 프로젝트 ID 형식을 확인해 주세요.")
    if not 1 <= args.per_gender <= 60:
        raise RuntimeError("--per-gender는 1~60 사이로 지정해 주세요.")

    planned = list(profiles(args.per_gender))
    print(f"프로젝트: {args.project_id}; 신규 테스트 프로필 최대 남 {args.per_gender}, 여 {args.per_gender}")
    print(f"ID 범위: {planned[0]['id']} ~ {planned[-1]['id']}")
    print("기존 참가자·상품 재고는 덮어쓰거나 초기화하지 않습니다.")
    if not args.apply:
        print("미리보기만 했습니다. --apply를 붙여야 Firebase에 기록합니다.")
        return

    active_project = gcloud("config", "get-value", "project")
    if active_project != args.project_id:
        raise RuntimeError(f"Cloud Shell 활성 프로젝트 {active_project!r}가 대상과 다릅니다.")
    token = gcloud("auth", "print-access-token")
    stats = get_document(args.project_id, token, "GameState/stats")
    if stats is None or not stats.get("fields", {}).get("totalRegistrations", {}).get("integerValue"):
        raise RuntimeError("GameState/stats의 totalRegistrations 정수 필드가 없습니다.")

    counts = {"created": 0, "existing": 0, "recovered": 0}
    for index, profile in enumerate(planned, 1):
        result = write_profile(args.project_id, token, profile)
        counts[result] += 1
        if index % 10 == 0 or index == len(planned):
            print(f"{index}/{len(planned)} 처리: 신규 {counts['created']}, 기존 {counts['existing']}, 응답유실 복구 {counts['recovered']}")
    print("완료. 남/여 테스트 프로필과 ParticipantKeys를 확인했습니다. 상품 재고 필드는 변경하지 않았습니다.")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError, ValueError, error.URLError) as exc:
        print(f"오류: {exc}", file=sys.stderr)
        sys.exit(1)
