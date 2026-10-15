using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Soro.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Protections qui ne s'expriment pas dans le modèle EF : isolation par commerce (RLS, RG-15)
    /// et écritures financières conservées (RG-02, RG-16).
    /// </summary>
    public partial class SecurityPolicies : Migration
    {
        private const string Tables =
            "'cash_register_sessions', 'sales', 'sale_lines', 'sale_payments', 'audit_entries'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Isolation : une ligne n'est visible et écrivable que pour le commerce posé dans app.tenant_id.
            // Sans contexte, NULLIF(...) vaut NULL et aucune ligne ne correspond. FORCE applique aussi la règle
            // au propriétaire des tables (seul un superutilisateur y échappe, jamais le rôle applicatif).
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY[{{Tables}}] LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I
                               USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)
                               WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                            table_name);
                    END LOOP;
                END $$;
                """);

            // Historique non modifiable : jamais de suppression, lignes de vente, paiements et audit figés.
            migrationBuilder.Sql("""
                CREATE FUNCTION forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% interdit sur % : l''historique est conservé', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = '42501';
                END $$;

                CREATE TRIGGER sessions_no_delete BEFORE DELETE ON cash_register_sessions
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER sales_no_delete BEFORE DELETE ON sales
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER sale_lines_immutable BEFORE UPDATE OR DELETE ON sale_lines
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER sale_payments_immutable BEFORE UPDATE OR DELETE ON sale_payments
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                CREATE TRIGGER audit_entries_immutable BEFORE UPDATE OR DELETE ON audit_entries
                    FOR EACH ROW EXECUTE FUNCTION forbid_change();
                """);

            // Une vente ne change que par son annulation, et une annulation est définitive.
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_sale() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.status = 'Cancelled' THEN
                        RAISE EXCEPTION 'Une vente annulée ne se modifie plus' USING ERRCODE = '42501';
                    END IF;
                    IF ROW(NEW.id, NEW.tenant_id, NEW.session_id, NEW.point_of_sale_id, NEW.register_id,
                           NEW.cashier_id, NEW.local_sequence, NEW.recorded_at, NEW.discount,
                           NEW.customer_id, NEW.gross_total, NEW.net_total)
                       IS DISTINCT FROM
                       ROW(OLD.id, OLD.tenant_id, OLD.session_id, OLD.point_of_sale_id, OLD.register_id,
                           OLD.cashier_id, OLD.local_sequence, OLD.recorded_at, OLD.discount,
                           OLD.customer_id, OLD.gross_total, OLD.net_total) THEN
                        RAISE EXCEPTION 'Seule l''annulation peut modifier une vente validée' USING ERRCODE = '42501';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER sales_protect BEFORE UPDATE ON sales
                    FOR EACH ROW EXECUTE FUNCTION protect_sale();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER sales_protect ON sales;
                DROP FUNCTION protect_sale();
                DROP TRIGGER audit_entries_immutable ON audit_entries;
                DROP TRIGGER sale_payments_immutable ON sale_payments;
                DROP TRIGGER sale_lines_immutable ON sale_lines;
                DROP TRIGGER sales_no_delete ON sales;
                DROP TRIGGER sessions_no_delete ON cash_register_sessions;
                DROP FUNCTION forbid_change();
                """);

            migrationBuilder.Sql($$"""
                DO $$
                DECLARE table_name text;
                BEGIN
                    FOREACH table_name IN ARRAY ARRAY[{{Tables}}] LOOP
                        EXECUTE format('DROP POLICY tenant_isolation ON %I', table_name);
                        EXECUTE format('ALTER TABLE %I NO FORCE ROW LEVEL SECURITY', table_name);
                        EXECUTE format('ALTER TABLE %I DISABLE ROW LEVEL SECURITY', table_name);
                    END LOOP;
                END $$;
                """);
        }
    }
}
