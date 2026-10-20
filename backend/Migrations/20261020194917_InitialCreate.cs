using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Felhasznalok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JelszoHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Nev = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SzuletesiDatum = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Felhasznalok", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rendszerparameterek",
                columns: table => new
                {
                    Kulcs = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Ertek = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Leiras = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ModositasIdeje = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rendszerparameterek", x => x.Kulcs);
                });

            migrationBuilder.CreateTable(
                name: "Termek",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nev = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kapacitas = table.Column<int>(type: "int", nullable: false),
                    Leiras = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Aktivalva = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Termek", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FelhasznaloId = table.Column<int>(type: "int", nullable: true),
                    MuveletTipus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntitasTipus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntitasId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RegiErtek = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UjErtek = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Indoklas = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LetrehozasIdeje = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogok", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogok_Felhasznalok_FelhasznaloId",
                        column: x => x.FelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Foglalasok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FelhasznaloId = table.Column<int>(type: "int", nullable: false),
                    TeremId = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foglalasok", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Foglalasok_Felhasznalok_FelhasznaloId",
                        column: x => x.FelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Foglalasok_Termek_TeremId",
                        column: x => x.TeremId,
                        principalTable: "Termek",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Hibajegyek",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FelhasznaloId = table.Column<int>(type: "int", nullable: false),
                    TeremId = table.Column<int>(type: "int", nullable: false),
                    Kategoria = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Leiras = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Statusz = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hibajegyek", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hibajegyek_Felhasznalok_FelhasznaloId",
                        column: x => x.FelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Hibajegyek_Termek_TeremId",
                        column: x => x.TeremId,
                        principalTable: "Termek",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KarbantartasiIdosavok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FelhasznaloId = table.Column<int>(type: "int", nullable: false),
                    TeremId = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Leiras = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KarbantartasiIdosavok", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KarbantartasiIdosavok_Felhasznalok_FelhasznaloId",
                        column: x => x.FelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KarbantartasiIdosavok_Termek_TeremId",
                        column: x => x.TeremId,
                        principalTable: "Termek",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Kulcsok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Szam = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tipus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Aktivalva = table.Column<bool>(type: "bit", nullable: false),
                    TeremId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kulcsok", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kulcsok_Termek_TeremId",
                        column: x => x.TeremId,
                        principalTable: "Termek",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ertesitesek",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FoglalasId = table.Column<int>(type: "int", nullable: false),
                    ErtesitesTipus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdoPont = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Allapot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ertesitesek", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ertesitesek_Foglalasok_FoglalasId",
                        column: x => x.FoglalasId,
                        principalTable: "Foglalasok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Kulcsmozgasok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KulcsId = table.Column<int>(type: "int", nullable: false),
                    FoglalasId = table.Column<int>(type: "int", nullable: true),
                    MozgasTipus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Idopont = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OperátorId = table.Column<int>(type: "int", nullable: false),
                    Megjegyzes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kulcsmozgasok", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kulcsmozgasok_Felhasznalok_OperátorId",
                        column: x => x.OperátorId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Kulcsmozgasok_Foglalasok_FoglalasId",
                        column: x => x.FoglalasId,
                        principalTable: "Foglalasok",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Kulcsmozgasok_Kulcsok_KulcsId",
                        column: x => x.KulcsId,
                        principalTable: "Kulcsok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KulcsTermek",
                columns: table => new
                {
                    KulcsId = table.Column<int>(type: "int", nullable: false),
                    TeremId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KulcsTermek", x => new { x.KulcsId, x.TeremId });
                    table.ForeignKey(
                        name: "FK_KulcsTermek_Kulcsok_KulcsId",
                        column: x => x.KulcsId,
                        principalTable: "Kulcsok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KulcsTermek_Termek_TeremId",
                        column: x => x.TeremId,
                        principalTable: "Termek",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MesterkulcsJogosultsagok",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FelhasznaloId = table.Column<int>(type: "int", nullable: false),
                    KulcsId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MesterkulcsJogosultsagok", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MesterkulcsJogosultsagok_Felhasznalok_FelhasznaloId",
                        column: x => x.FelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MesterkulcsJogosultsagok_Kulcsok_KulcsId",
                        column: x => x.KulcsId,
                        principalTable: "Kulcsok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MegbizhatosagiEsemenyek",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FelhasznaloId = table.Column<int>(type: "int", nullable: false),
                    KulcsId = table.Column<int>(type: "int", nullable: true),
                    FoglalasId = table.Column<int>(type: "int", nullable: true),
                    KiadasiKulcsmozgasId = table.Column<int>(type: "int", nullable: true),
                    EsemenyTipus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LetrehozoFelhasznaloId = table.Column<int>(type: "int", nullable: true),
                    Indoklas = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EredetiHatarido = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenylegesLeadasIdeje = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Pont = table.Column<int>(type: "int", nullable: false),
                    EsemenyIdopontja = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Tanev = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MegbizhatosagiEsemenyek", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MegbizhatosagiEsemenyek_Felhasznalok_FelhasznaloId",
                        column: x => x.FelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MegbizhatosagiEsemenyek_Felhasznalok_LetrehozoFelhasznaloId",
                        column: x => x.LetrehozoFelhasznaloId,
                        principalTable: "Felhasznalok",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MegbizhatosagiEsemenyek_Foglalasok_FoglalasId",
                        column: x => x.FoglalasId,
                        principalTable: "Foglalasok",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MegbizhatosagiEsemenyek_Kulcsmozgasok_KiadasiKulcsmozgasId",
                        column: x => x.KiadasiKulcsmozgasId,
                        principalTable: "Kulcsmozgasok",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MegbizhatosagiEsemenyek_Kulcsok_KulcsId",
                        column: x => x.KulcsId,
                        principalTable: "Kulcsok",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Rendszerparameterek",
                columns: new[] { "Kulcs", "Ertek", "Leiras", "ModositasIdeje" },
                values: new object[,]
                {
                    { "napi_kulcsleadasi_zarasi_idopont", "22:00", "Napi kulcsleadási zárási időpont", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "no_show_idokorlat_perc", "15", "No-show időkorlát percben", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogok_FelhasznaloId",
                table: "AuditLogok",
                column: "FelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_Ertesitesek_FoglalasId",
                table: "Ertesitesek",
                column: "FoglalasId");

            migrationBuilder.CreateIndex(
                name: "IX_Felhasznalok_Email",
                table: "Felhasznalok",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Felhasznalok_Username",
                table: "Felhasznalok",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foglalasok_FelhasznaloId",
                table: "Foglalasok",
                column: "FelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_Foglalasok_TeremId",
                table: "Foglalasok",
                column: "TeremId");

            migrationBuilder.CreateIndex(
                name: "IX_Hibajegyek_FelhasznaloId",
                table: "Hibajegyek",
                column: "FelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_Hibajegyek_TeremId",
                table: "Hibajegyek",
                column: "TeremId");

            migrationBuilder.CreateIndex(
                name: "IX_KarbantartasiIdosavok_FelhasznaloId",
                table: "KarbantartasiIdosavok",
                column: "FelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_KarbantartasiIdosavok_TeremId",
                table: "KarbantartasiIdosavok",
                column: "TeremId");

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsmozgasok_FoglalasId",
                table: "Kulcsmozgasok",
                column: "FoglalasId");

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsmozgasok_KulcsId",
                table: "Kulcsmozgasok",
                column: "KulcsId");

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsmozgasok_OperátorId",
                table: "Kulcsmozgasok",
                column: "OperátorId");

            migrationBuilder.CreateIndex(
                name: "IX_Kulcsok_TeremId",
                table: "Kulcsok",
                column: "TeremId");

            migrationBuilder.CreateIndex(
                name: "IX_KulcsTermek_TeremId",
                table: "KulcsTermek",
                column: "TeremId");

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_FelhasznaloId",
                table: "MegbizhatosagiEsemenyek",
                column: "FelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_FoglalasId",
                table: "MegbizhatosagiEsemenyek",
                column: "FoglalasId");

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_KiadasiKulcsmozgasId",
                table: "MegbizhatosagiEsemenyek",
                column: "KiadasiKulcsmozgasId");

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_KulcsId",
                table: "MegbizhatosagiEsemenyek",
                column: "KulcsId");

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_LetrehozoFelhasznaloId",
                table: "MegbizhatosagiEsemenyek",
                column: "LetrehozoFelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_MesterkulcsJogosultsagok_FelhasznaloId",
                table: "MesterkulcsJogosultsagok",
                column: "FelhasznaloId");

            migrationBuilder.CreateIndex(
                name: "IX_MesterkulcsJogosultsagok_KulcsId",
                table: "MesterkulcsJogosultsagok",
                column: "KulcsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogok");

            migrationBuilder.DropTable(
                name: "Ertesitesek");

            migrationBuilder.DropTable(
                name: "Hibajegyek");

            migrationBuilder.DropTable(
                name: "KarbantartasiIdosavok");

            migrationBuilder.DropTable(
                name: "KulcsTermek");

            migrationBuilder.DropTable(
                name: "MegbizhatosagiEsemenyek");

            migrationBuilder.DropTable(
                name: "MesterkulcsJogosultsagok");

            migrationBuilder.DropTable(
                name: "Rendszerparameterek");

            migrationBuilder.DropTable(
                name: "Kulcsmozgasok");

            migrationBuilder.DropTable(
                name: "Foglalasok");

            migrationBuilder.DropTable(
                name: "Kulcsok");

            migrationBuilder.DropTable(
                name: "Felhasznalok");

            migrationBuilder.DropTable(
                name: "Termek");
        }
    }
}
