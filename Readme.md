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
- `GET /api/integrations/status`: 설정된 외부 API 연결 확인

외부 API는 `ExternalApi:BaseUrl`과 `ExternalApi:HealthPath`로 설정합니다. 사내 프록시와 방화벽 허용 정책은 서버 환경에 맞춰 별도로 적용해야 합니다.

## 보안

급여 권한이 없는 사용자에게는 API 응답에서도 급여 값과 집계를 반환하지 않으며, 인증된 API 호출은 SQLite의 `AuditEvents` 테이블에 기록됩니다.
