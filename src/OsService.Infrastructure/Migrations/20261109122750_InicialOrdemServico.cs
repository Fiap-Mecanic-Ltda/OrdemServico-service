using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OsService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InicialOrdemServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "os");

            migrationBuilder.CreateTable(
                name: "OrdemServico",
                schema: "os",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    VeiculoId = table.Column<int>(type: "int", nullable: false),
                    DescricaoProblema = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ValorTotalEstimado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DataCriacao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataModificacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataAlteracaoStatus = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataInicioExecucao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataFimExecucao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrcamentoId = table.Column<int>(type: "int", nullable: true),
                    PagamentoId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdemServico", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoricoStatusOrdemServico",
                schema: "os",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdemServicoId = table.Column<int>(type: "int", nullable: false),
                    StatusAnterior = table.Column<int>(type: "int", nullable: true),
                    StatusNovo = table.Column<int>(type: "int", nullable: false),
                    DataAlteracao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Origem = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricoStatusOrdemServico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricoStatusOrdemServico_OrdemServico_OrdemServicoId",
                        column: x => x.OrdemServicoId,
                        principalSchema: "os",
                        principalTable: "OrdemServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemOrdemServico",
                schema: "os",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdemServicoId = table.Column<int>(type: "int", nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantidade = table.Column<int>(type: "int", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EstoqueId = table.Column<int>(type: "int", nullable: true),
                    ServicoOficinaId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemOrdemServico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemOrdemServico_OrdemServico_OrdemServicoId",
                        column: x => x.OrdemServicoId,
                        principalSchema: "os",
                        principalTable: "OrdemServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricoStatusOrdemServico_OrdemServicoId_DataAlteracao",
                schema: "os",
                table: "HistoricoStatusOrdemServico",
                columns: new[] { "OrdemServicoId", "DataAlteracao" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemOrdemServico_OrdemServicoId",
                schema: "os",
                table: "ItemOrdemServico",
                column: "OrdemServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdemServico_ClienteId",
                schema: "os",
                table: "OrdemServico",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdemServico_CorrelationId",
                schema: "os",
                table: "OrdemServico",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdemServico_Status",
                schema: "os",
                table: "OrdemServico",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricoStatusOrdemServico",
                schema: "os");

            migrationBuilder.DropTable(
                name: "ItemOrdemServico",
                schema: "os");

            migrationBuilder.DropTable(
                name: "OrdemServico",
                schema: "os");
        }
    }
}
