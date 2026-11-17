using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OsService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SagaDadosOrcamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelefoneCliente",
                schema: "os",
                table: "SagaOrdemServico",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VeiculoAno",
                schema: "os",
                table: "SagaOrdemServico",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VeiculoMarca",
                schema: "os",
                table: "SagaOrdemServico",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VeiculoModelo",
                schema: "os",
                table: "SagaOrdemServico",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VeiculoPlaca",
                schema: "os",
                table: "SagaOrdemServico",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TelefoneCliente",
                schema: "os",
                table: "SagaOrdemServico");

            migrationBuilder.DropColumn(
                name: "VeiculoAno",
                schema: "os",
                table: "SagaOrdemServico");

            migrationBuilder.DropColumn(
                name: "VeiculoMarca",
                schema: "os",
                table: "SagaOrdemServico");

            migrationBuilder.DropColumn(
                name: "VeiculoModelo",
                schema: "os",
                table: "SagaOrdemServico");

            migrationBuilder.DropColumn(
                name: "VeiculoPlaca",
                schema: "os",
                table: "SagaOrdemServico");
        }
    }
}
