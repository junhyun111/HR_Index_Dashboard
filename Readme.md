# HR Index Dashboard

.NET 10 ASP.NET Core, EF Core, SQLite 기반 사내 HR 대시보드입니다.

## 로컬 실행

```powershell
dotnet restore
dotnet run
```

`http://localhost:5080`으로 접속합니다. Development 환경은 현재 Windows 사용자를 모든 권한의 개발 사용자로 자동 로그인시킵니다. 대시보드는 `App_Data/hr-dashboard.db`의 데이터를 사용합니다.

## 운영 AD 설정

`appsettings.Production.json`은 `Authentication:Mode`가 `Windows`입니다. `appsettings.json`의 그룹 이름을 실제 AD 그룹에 맞게 변경합니다.

- `DashboardViewer`: 일반 대시보드 조회
- `SalaryViewer`: 급여 원본과 급여 집계 조회
- `Editor`: 데이터 변경
- `Administrator`: 외부 연동 상태 확인

IIS에서는 Windows Authentication을 켜고 Anonymous Authentication을 끕니다. 애플리케이션 풀 계정에는 `App_Data` 폴더의 읽기/쓰기 권한이 필요합니다. SQLite 파일은 네트워크 공유 폴더가 아닌 서버 로컬 디스크에 둡니다.

## 주요 API

- `GET /api/session`: 로그인 사용자 및 권한
- `GET /api/dashboard`: 필터, 집계, 직원 페이지 조회
- `GET /api/employees/export`: 직원 데이터를 UTF-8 `.csv`로 내려받기 (`Editor` 권한)
- `POST /api/employees/import`: 수정된 `.csv`를 검증한 뒤 직원 추가/수정 (`Editor` 권한)
- `POST /api/employees/paste`: Excel에서 복사한 탭 구분 표를 검증한 뒤 직원 추가/수정 (`Editor` 권한)
- `GET /api/integrations/status`: 설정된 외부 API 연결 확인

외부 API는 `ExternalApi:BaseUrl`과 `ExternalApi:HealthPath`로 설정합니다. 사내 프록시와 방화벽 허용 정책은 서버 환경에 맞춰 별도로 적용해야 합니다.

## 보안

급여 권한이 없는 사용자에게는 API 응답에서도 급여 값과 집계를 반환하지 않으며, 인증된 API 호출은 SQLite의 `AuditEvents` 테이블에 기록됩니다.

CSV 가져오기는 `직원 ID`가 있는 행을 수정하고 ID가 빈 행을 새 직원으로 추가합니다. 파일에서 삭제한 행은 DB에서 삭제되지 않습니다. 급여 조회 권한이 없는 편집자는 내보낸 파일에서 월 임금이 비어 있으며 기존 급여도 변경되지 않습니다.

DRM 환경에서는 CSV를 Excel로 연 뒤 머리글을 포함한 표 전체를 복사하여 `Excel 붙여넣기` 창에 붙여넣을 수 있습니다. 붙여넣은 데이터는 파일 업로드 없이 JSON 요청으로 전송됩니다.
