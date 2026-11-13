# cicd

ASP.NET Core 10 MVC with SQL Server/Azure SQL, Entity Framework Core migrations,
ASP.NET Core Identity, and permission policies.

## Roles and permissions

| Role | Permission claims |
| --- | --- |
| Admin | `profile.view`, `users.view`, `roles.view` |
| Manager | `profile.view`, `users.view` |
| Customer | `profile.view` |

The claim type is `permission`. Roles store these claims in `AspNetRoleClaims`;
Identity includes role claims in the user's authentication cookie at sign-in.
Controllers enforce policies even if someone navigates directly to a URL.
`/Account/Profile` shows the current user's permissions, `/Users` lists users,
and `/Roles` lists roles and their permissions. There is no public registration
or account/role editing UI.

`IUserRepository` / `UserRepository` implement paginated, read-only EF queries.
Use `UserManager<ApplicationUser>` and `RoleManager<IdentityRole>` for Identity
writes so password hashing, validation, normalization, and security stamps are preserved.
Add application-specific repositories as domain entities are introduced.

## Local setup

Requires .NET 10 SDK, SQL Server LocalDB (Visual Studio installer component),
and a trusted ASP.NET Core HTTPS development certificate.

```powershell
dotnet restore cicd.sln
dotnet dev-certs https --trust
dotnet user-secrets set "IdentitySeed:Enabled" "true"
dotnet user-secrets set "IdentitySeed:Users:0:Password" "<choose-a-unique-admin-password>"
dotnet user-secrets set "IdentitySeed:Users:1:Password" "<choose-a-unique-customer-password>"
dotnet run --launch-profile https -- --initialize-database
dotnet run --launch-profile https
```

Replace password placeholders locally. Each password must have at least 12
characters, including upper/lower case, a number, and a nonalphanumeric character.
Never commit real passwords or connection strings. `admin@example.com` and
`customer@example.com` are the default accounts; override `IdentitySeed:Users:0:Email`
and `IdentitySeed:Users:1:Email` in user secrets to use your own addresses before initialization.
These accounts are not marked email-confirmed; email confirmation is not required
for this initial login-only flow. No email sender/reset-password flow is configured.

`--initialize-database` applies migrations, creates all three roles and their
permission claims, optionally seeds the two configured users, then exits.
With `IdentitySeed:Enabled=false` it only creates schema/roles/claims.
Normal application startup never migrates or seeds the database.
The initializer is repeatable: it adds missing roles/claims/users/role memberships,
preserves existing passwords, and does not delete existing users or custom claims.
Run only one initializer against a database at a time.

For another SQL Server, supply `ConnectionStrings:DefaultConnection` through
user secrets. Development falls back to a LocalDB database named `cicdIdentity`.
Production requires an explicit connection string.

## Azure staging and production

1. Create separate Azure SQL databases for staging and production.
2. In **each slot's** Azure application configuration, set
   `ConnectionStrings__DefaultConnection` to that slot's database connection string.
   Mark it as a **Deployment slot setting**, so the database stays with its slot
   during a swap. Use encrypted SQL connections and credentials/managed identity
   with the required SQL database permissions. Do not put secrets in YAML.
3. Before releasing this version, initialize **both databases** from a trusted
   environment with database network access, using the matching application's code:

   ```powershell
   # Supply these environment variables securely through your environment/secret manager:
   # ASPNETCORE_ENVIRONMENT=Production
   # ConnectionStrings__DefaultConnection=<target database connection string>
   # IdentitySeed__Enabled=true
   # IdentitySeed__Users__0__Password=<unique admin password>
   # IdentitySeed__Users__1__Password=<unique customer password>
   dotnet run --configuration Release --no-launch-profile -- --initialize-database
   # Or, from the published app directory:
   dotnet cicd.dll --initialize-database
   ```

   Use staging secrets for the staging database and production secrets for the
   production database. Defaults for emails/roles come from appsettings.json;
   override them before seeding as needed. After bootstrap, remove seed passwords
   from the initialization environment and set `IdentitySeed__Enabled=false`.
   Use a separate migration identity with schema-change permissions if runtime
   access is restricted. The application identity needs read/write Identity tables.
4. Keep App Service **auto swap disabled**. This pipeline performs the swap after
   its own checks. Configure .NET 10 in both slots and authorize the service connection.
5. Push all files to `master`. The pipeline builds, tests, deploys staging, and
   checks `/health/ready` before swapping staging into production. This endpoint
   checks the Identity database/tables and roles; it returns no account details.
   Initialize production before the swap: a staging check cannot validate the
   separate production database. The previous production code moves to staging.

Database migrations are deliberately a separate initialization step rather than
running on every web-server startup. Future releases must apply backward-compatible
migrations to both databases before promotion; a slot rollback does not roll back
database schema. Database credentials, Azure resources, and remote migrations
are not provisioned by this repository.

For an approval pause, configure **Approvals and checks** on the Azure DevOps
`cicd-production` environment. Otherwise production promotion is automatic.
Batched CI prevents overlapping push-triggered releases; avoid overlapping manually
queued runs against the same staging slot.

## Migrations and tests

```powershell
dotnet tool restore
dotnet ef migrations add YourMigration -- --environment Development
dotnet ef migrations script --idempotent --output migration.sql -- --environment Development
dotnet test cicd.sln --configuration Release
```

Tests use an isolated EF in-memory database to verify seed repeatability, password
validation, role membership, permission enforcement, and repository queries. They
do not replace applying/testing migrations against SQL Server. After role/claim
changes, sign out and back in to refresh an existing authentication cookie.
