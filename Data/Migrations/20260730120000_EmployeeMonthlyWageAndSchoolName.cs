using HRDashboard.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRDashboard.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260730120000_EmployeeMonthlyWageAndSchoolName")]
public sealed class EmployeeMonthlyWageAndSchoolName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "MonthlyWage",
            table: "Employees",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SchoolName",
            table: "Employees",
            type: "TEXT",
            maxLength: 150,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MonthlyWage",
            table: "Employees");

        migrationBuilder.DropColumn(
            name: "SchoolName",
            table: "Employees");
    }
}
