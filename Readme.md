# HR Index Dashboard

.NET 10 ASP.NET Core, EF Core, SQLite 기반 사내 HR 대시보드입니다.

## 로컬 실행

```powershell
dotnet restore
dotnet run
```

`http://localhost:5080`으로 접속합니다. 로그인 페이지에서 백엔드에 설정된 공용 계정으로 인증하며, 로그인한 사용자는 별도 역할 구분 없이 모든 기능을 사용할 수 있습니다. 일별 사원 DB는 `App_Data/employee-daily/employeeYYMMDD.db` 형식으로 저장됩니다.

## 로그인 및 권한

계정은 `App_Data/common-settings.db`에서 관리합니다. 계정이 하나도 없을 때 최초 관리자 `admin / 1234`가 생성되며, 로그인 후 설정 화면에서 반드시 변경하는 것을 권장합니다. 비밀번호는 PBKDF2-SHA256 해시로 저장됩니다.

관리자는 계정과 사원 DB 설정을 관리하고 사원 데이터를 변경할 수 있습니다. 일반 사용자 계정은 `@innodep.com` 이메일만 사용할 수 있으며, 대시보드를 조회하고 본인의 로그인 정보와 화면 모드만 변경할 수 있습니다. IIS에서는 Anonymous Authentication을 켜고 Windows Authentication을 끕니다. 애플리케이션 풀 계정에는 `App_Data` 폴더의 읽기/쓰기 권한이 필요합니다.

## 주요 API

- `POST /api/auth/login`: 로그인
- `POST /api/auth/logout`: 로그아웃
- `GET /api/session`: 로그인 사용자 조회
- `PUT /api/settings/profile`: 기존 비밀번호 확인 후 본인 로그인 정보 변경
- `PUT /api/settings/theme`: 계정별 라이트·다크 모드 저장
- `GET/POST/PUT/DELETE /api/settings/accounts`: 관리자 전용 계정 및 권한 관리
- `GET /api/settings/database-history`: 관리자 전용 사원 DB 업데이트 이력
- `GET /api/dashboard`: 필터, 집계, 직원 페이지 조회
- `GET /api/employees/headcount-trend`: 최근 12개월 또는 15일의 날짜별 DB 인원 추이 조회
- `GET /api/employees/export`: 직원 데이터를 Excel `.xlsx`로 내려받기
- `POST /api/employees/import`: 수정된 `.csv`를 검증한 뒤 직원 추가/수정
- `POST /api/employees/paste`: Excel에서 복사한 탭 구분 표를 검증한 뒤 직원 추가/수정
- `GET /api/settings/employee-columns`: 공통 사원 DB 열 이름 설정 조회
- `PUT /api/settings/employee-columns`: 공통 사원 DB 열 이름 설정 저장
- `POST /api/settings/employee-columns/reset`: 사원 DB 열 이름 기본값 복원
- `GET /api/integrations/status`: 설정된 외부 API 연결 확인

외부 API는 `ExternalApi:BaseUrl`과 `ExternalApi:HealthPath`로 설정합니다. 사내 프록시와 방화벽 허용 정책은 서버 환경에 맞춰 별도로 적용해야 합니다.

## 보안

로그인 쿠키는 HttpOnly와 SameSite=Lax로 설정되며 로그인 시도는 IP별 분당 5회로 제한됩니다.

CSV 가져오기는 `직원 ID`가 있는 행을 수정하고 ID가 빈 행을 새 직원으로 추가합니다. 파일에서 삭제한 행은 DB에서 삭제되지 않습니다.

DRM 환경에서는 CSV를 Excel로 연 뒤 머리글을 포함한 표 전체를 복사하여 `Excel 붙여넣기` 창에 붙여넣을 수 있습니다. 붙여넣은 데이터는 파일 업로드 없이 JSON 요청으로 전송됩니다.

계정, 권한, 화면 모드, 사원 DB 열의 사용자 표시 이름과 DB 업데이트 이력은 날짜별 사원 DB와 분리된 `App_Data/common-settings.db`에 저장됩니다. 내부 필드명은 변경하지 않으므로 그래프, 필터, 검색과 정렬 계산에는 영향을 주지 않습니다.

기존 `App_Data` 최상위 경로에 있는 `employeeYYMMDD.db` 파일은 애플리케이션 시작 시 `App_Data/employee-daily` 폴더로 자동 이전됩니다.
