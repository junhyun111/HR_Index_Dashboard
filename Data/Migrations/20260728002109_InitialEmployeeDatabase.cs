using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRDashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialEmployeeDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeDataState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeDataState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Workplace = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ParentDepartment = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Position = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    WorkShift = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Duty = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    JobGroup = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EmploymentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BirthDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HireDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TerminationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MonthlyWage = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Department",
                table: "Employees",
                column: "Department");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_EmployeeNumber",
                table: "Employees",
                column: "EmployeeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Name",
                table: "Employees",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeDataState");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
