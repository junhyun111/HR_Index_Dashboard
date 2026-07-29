using HRDashboard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRDashboard.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260729090000_AnnualSalaryEducationAndMajor")]
public sealed class AnnualSalaryEducationAndMajor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "MonthlyWage",
            table: "Employees",
            newName: "AnnualSalary");

        // 기존 값은 월임금이므로 연봉의 의미를 유지하도록 12개월분으로 환산한다.
        migrationBuilder.Sql("""
            UPDATE Employees
            SET AnnualSalary = AnnualSalary * 12
            WHERE AnnualSalary IS NOT NULL;
            """);

        migrationBuilder.AddColumn<string>(
            name: "Education",
            table: "Employees",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Major",
            table: "Employees",
            type: "TEXT",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Education",
            table: "Employees");

        migrationBuilder.DropColumn(
            name: "Major",
            table: "Employees");

        migrationBuilder.Sql("""
            UPDATE Employees
            SET AnnualSalary = AnnualSalary / 12
            WHERE AnnualSalary IS NOT NULL;
            """);

        migrationBuilder.RenameColumn(
            name: "AnnualSalary",
            table: "Employees",
            newName: "MonthlyWage");
    }
}
