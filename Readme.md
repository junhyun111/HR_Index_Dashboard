# HR Index Dashboard

.NET 10 ASP.NET Core, EF Core, SQLite 기반 사내 HR 대시보드입니다.

## 로컬 실행

```powershell
dotnet restore
dotnet run
```

`http://localhost:5080`으로 접속합니다. 로그인 페이지에서 백엔드에 설정된 공용 계정으로 인증하며, 로그인한 사용자는 별도 역할 구분 없이 모든 기능을 사용할 수 있습니다. 대시보드는 `App_Data/hr-dashboard.db`의 데이터를 사용합니다.

## 로그인 설정

로그인 계정은 `appsettings.json`의 `Authentication`에서 관리합니다.

```json
"Authentication": {
  "UserName": "innodep1!",
  "Password": "Innodep1!"
}
```

운영 서버에서는 `Authentication__UserName`, `Authentication__Password` 환경 변수로 덮어쓰는 방식을 권장합니다. IIS에서는 Anonymous Authentication을 켜고 Windows Authentication을 끕니다. 애플리케이션 풀 계정에는 `App_Data` 폴더의 읽기/쓰기 권한이 필요합니다. SQLite 파일은 네트워크 공유 폴더가 아닌 서버 로컬 디스크에 둡니다.

## 주요 API

- `POST /api/auth/login`: 로그인
- `POST /api/auth/logout`: 로그아웃
- `GET /api/session`: 로그인 사용자 조회
- `GET /api/dashboard`: 필터, 집계, 직원 페이지 조회
- `GET /api/employees/export`: 직원 데이터를 UTF-8 `.csv`로 내려받기
- `POST /api/employees/import`: 수정된 `.csv`를 검증한 뒤 직원 추가/수정
- `POST /api/employees/paste`: Excel에서 복사한 탭 구분 표를 검증한 뒤 직원 추가/수정
- `GET /api/integrations/status`: 설정된 외부 API 연결 확인

외부 API는 `ExternalApi:BaseUrl`과 `ExternalApi:HealthPath`로 설정합니다. 사내 프록시와 방화벽 허용 정책은 서버 환경에 맞춰 별도로 적용해야 합니다.

## 보안

로그인 쿠키는 HttpOnly와 SameSite=Lax로 설정되며 로그인 시도는 IP별 분당 5회로 제한됩니다. 인증된 API 호출은 SQLite의 `AuditEvents` 테이블에 기록됩니다.

CSV 가져오기는 `직원 ID`가 있는 행을 수정하고 ID가 빈 행을 새 직원으로 추가합니다. 파일에서 삭제한 행은 DB에서 삭제되지 않습니다.

DRM 환경에서는 CSV를 Excel로 연 뒤 머리글을 포함한 표 전체를 복사하여 `Excel 붙여넣기` 창에 붙여넣을 수 있습니다. 붙여넣은 데이터는 파일 업로드 없이 JSON 요청으로 전송됩니다.
