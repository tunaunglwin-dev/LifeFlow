using System.Security.Cryptography;
using System.Text;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var rawConnectionString = builder.Configuration.GetConnectionString("Default") ?? Environment.GetEnvironmentVariable("DATABASE_URL");
var connectionString = string.IsNullOrWhiteSpace(rawConnectionString) ? null : NormalizeConnectionString(rawConnectionString);
var corsOrigin = builder.Configuration["CorsOrigin"] ?? Environment.GetEnvironmentVariable("CORS_ORIGIN") ?? "*";

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigin == "*") policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else policy.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();

var demoUsers = new List<(int Id, string Name, string Password)> { (1, "admin", "123456") };
var demoDonors = new List<Donor> { new(1, "Aung Aung", 22, "0912345678", "Male", "A+", "Yangon") };
var demoStock = new List<Stock> { new(1, "Aung Aung", "A+", 8), new(2, "Su Su", "O+", 4) };
var demoRequests = new List<BloodRequest> { new(1, "Mg Mg", "General Hospital", "A+", 2, "Pending") };
var demoTransfers = new List<TransferReport>();
var nextDonorId = 2;
var nextStockId = 3;
var nextRequestId = 2;
var nextTransferId = 1;

app.MapGet("/", () => Results.Ok(new { name = "Blood Bank API", status = "running", mode = connectionString is null ? "demo" : "database" }));

app.MapPost("/api/register", async (LoginDto dto) =>
{
    dto = CleanLogin(dto);
    if (dto.Password.Length < 6) return Results.BadRequest(new { error = "Password must be at least 6 characters." });
    if (connectionString is null)
    {
        if (demoUsers.Any(user => user.Name.Equals(dto.Name, StringComparison.OrdinalIgnoreCase)))
            return Results.BadRequest(new { error = "User name already exists." });
        var id = demoUsers.Max(user => user.Id) + 1;
        demoUsers.Add((id, dto.Name, dto.Password));
        return Results.Ok(new { message = "Registered successfully." });
    }
    await using var db = new NpgsqlConnection(connectionString);
    await db.OpenAsync();
    await using var exists = new NpgsqlCommand("select id from users where lower(name)=lower(@name)", db);
    exists.Parameters.AddWithValue("name", dto.Name);
    if (await exists.ExecuteScalarAsync() is not null) return Results.BadRequest(new { error = "User name already exists." });
    await using var cmd = new NpgsqlCommand("insert into users(name,password_hash) values(@name,@password)", db);
    cmd.Parameters.AddWithValue("name", dto.Name);
    cmd.Parameters.AddWithValue("password", HashPassword(dto.Password));
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { message = "Registered successfully." });
});

app.MapPost("/api/login", async (LoginDto dto) =>
{
    dto = CleanLogin(dto);
    if (connectionString is null)
    {
        var user = demoUsers.FirstOrDefault(user => user.Name.Equals(dto.Name, StringComparison.OrdinalIgnoreCase) && user.Password == dto.Password);
        return user.Id == 0 ? Results.BadRequest(new { error = "User name and password do not match." }) : Results.Ok(new { id = user.Id, name = user.Name });
    }
    await using var db = new NpgsqlConnection(connectionString);
    await db.OpenAsync();
    await using var cmd = new NpgsqlCommand("select id,name,password_hash from users where lower(name)=lower(@name)", db);
    cmd.Parameters.AddWithValue("name", dto.Name);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || !VerifyPassword(dto.Password, reader.GetString(2)))
        return Results.BadRequest(new { error = "User name and password do not match." });
    return Results.Ok(new { id = reader.GetInt32(0), name = reader.GetString(1) });
});

app.MapGet("/api/dashboard", async () =>
{
    if (connectionString is null)
    {
        return Results.Ok(new
        {
            donors = demoDonors.Count,
            stockUnits = demoStock.Sum(item => item.Units),
            requests = demoRequests.Count(item => item.Status != "Completed"),
            transfers = demoTransfers.Count
        });
    }
    await using var db = new NpgsqlConnection(connectionString);
    await db.OpenAsync();
    return Results.Ok(new
    {
        donors = await ScalarInt(db, "select count(*) from donors"),
        stockUnits = await ScalarInt(db, "select coalesce(sum(units),0) from blood_stock"),
        requests = await ScalarInt(db, "select count(*) from blood_requests where status <> 'Completed'"),
        transfers = await ScalarInt(db, "select count(*) from blood_transfers")
    });
});

app.MapGet("/api/donors", async () => connectionString is null ? Results.Ok(demoDonors.OrderByDescending(d => d.Id)) : Results.Ok(await Query(connectionString, "select * from donors order by id desc")));
app.MapPost("/api/donors", async (Donor d) =>
{
    if (connectionString is not null) return await SaveDonor(connectionString, d);
    var saved = d with { Id = nextDonorId++ };
    demoDonors.Add(saved);
    return Results.Ok(saved);
});
app.MapPut("/api/donors/{id:int}", async (int id, Donor d) =>
{
    if (connectionString is not null) return await SaveDonor(connectionString, d with { Id = id });
    var index = demoDonors.FindIndex(item => item.Id == id);
    if (index >= 0) demoDonors[index] = d with { Id = id };
    return Results.Ok();
});
app.MapDelete("/api/donors/{id:int}", async (int id) =>
{
    if (connectionString is not null) return await Delete(connectionString, "donors", id);
    demoDonors.RemoveAll(item => item.Id == id);
    return Results.Ok();
});

app.MapGet("/api/stock", async () => connectionString is null ? Results.Ok(demoStock.OrderByDescending(s => s.Id).Select(s => new { s.Id, s.DonorName, s.BloodType, s.Units, Status = StockStatus(s.Units) })) : Results.Ok(await Query(connectionString, "select * from blood_stock order by id desc")));
app.MapPost("/api/stock", async (Stock s) =>
{
    if (connectionString is not null) return await SaveStock(connectionString, s);
    var saved = s with { Id = nextStockId++ };
    demoStock.Add(saved);
    return Results.Ok(saved);
});
app.MapPut("/api/stock/{id:int}", async (int id, Stock s) =>
{
    if (connectionString is not null) return await SaveStock(connectionString, s with { Id = id });
    var index = demoStock.FindIndex(item => item.Id == id);
    if (index >= 0) demoStock[index] = s with { Id = id };
    return Results.Ok();
});
app.MapDelete("/api/stock/{id:int}", async (int id) =>
{
    if (connectionString is not null) return await Delete(connectionString, "blood_stock", id);
    demoStock.RemoveAll(item => item.Id == id);
    return Results.Ok();
});

app.MapGet("/api/requests", async () => connectionString is null ? Results.Ok(demoRequests.OrderByDescending(r => r.Id)) : Results.Ok(await Query(connectionString, "select * from blood_requests order by id desc")));
app.MapPost("/api/requests", async (BloodRequest r) =>
{
    if (connectionString is not null) return await SaveRequest(connectionString, r);
    var saved = r with { Id = nextRequestId++ };
    demoRequests.Add(saved);
    return Results.Ok(saved);
});
app.MapPut("/api/requests/{id:int}", async (int id, BloodRequest r) =>
{
    if (connectionString is not null) return await SaveRequest(connectionString, r with { Id = id });
    var index = demoRequests.FindIndex(item => item.Id == id);
    if (index >= 0) demoRequests[index] = r with { Id = id };
    return Results.Ok();
});
app.MapDelete("/api/requests/{id:int}", async (int id) =>
{
    if (connectionString is not null) return await Delete(connectionString, "blood_requests", id);
    demoRequests.RemoveAll(item => item.Id == id);
    return Results.Ok();
});

app.MapGet("/api/reports", async () => connectionString is null ? Results.Ok(demoTransfers.OrderByDescending(t => t.Id)) : Results.Ok(await Query(connectionString, "select * from blood_transfers order by transferred_at desc,id desc")));

app.MapPost("/api/transfers", async (Transfer t) =>
{
    if (string.IsNullOrWhiteSpace(t.PatientName) || string.IsNullOrWhiteSpace(t.HospitalName) || !ValidBlood(t.BloodType) || t.Units <= 0)
        return Results.BadRequest(new { error = "Patient, hospital, blood type, and units are required." });
    if (connectionString is null)
    {
        var total = demoStock.Where(item => item.BloodType == t.BloodType).Sum(item => item.Units);
        if (total < t.Units) return Results.BadRequest(new { error = $"Only {total} units available for {t.BloodType}." });
        var remaining = t.Units;
        foreach (var stock in demoStock.Where(item => item.BloodType == t.BloodType && item.Units > 0).OrderBy(item => item.Id).ToList())
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, stock.Units);
            var index = demoStock.FindIndex(item => item.Id == stock.Id);
            demoStock[index] = stock with { Units = stock.Units - take };
            remaining -= take;
        }
        demoTransfers.Add(new(nextTransferId++, t.PatientName.Trim(), t.HospitalName.Trim(), t.BloodType, t.Units, DateTimeOffset.Now));
        return Results.Ok(new { message = "Blood issued successfully." });
    }
    await using var db = new NpgsqlConnection(connectionString);
    await db.OpenAsync();
    await using var tx = await db.BeginTransactionAsync();
    try
    {
        var total = await ScalarIntTx(db, tx, "select coalesce(sum(units),0) from blood_stock where blood_type=@blood", ("blood", t.BloodType));
        if (total < t.Units)
        {
            await tx.RollbackAsync();
            return Results.BadRequest(new { error = $"Only {total} units available for {t.BloodType}." });
        }
        var remaining = t.Units;
        await using (var stockCmd = new NpgsqlCommand("select id,units from blood_stock where blood_type=@blood and units>0 order by collected_at,id for update", db, tx))
        {
            stockCmd.Parameters.AddWithValue("blood", t.BloodType);
            await using var reader = await stockCmd.ExecuteReaderAsync();
            var rows = new List<(int Id, int Units)>();
            while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetInt32(1)));
            await reader.CloseAsync();
            foreach (var row in rows)
            {
                if (remaining <= 0) break;
                var take = Math.Min(remaining, row.Units);
                var newUnits = row.Units - take;
                await using var update = new NpgsqlCommand("update blood_stock set units=@units,status=@status where id=@id", db, tx);
                update.Parameters.AddWithValue("units", newUnits);
                update.Parameters.AddWithValue("status", StockStatus(newUnits));
                update.Parameters.AddWithValue("id", row.Id);
                await update.ExecuteNonQueryAsync();
                remaining -= take;
            }
        }
        await using var insert = new NpgsqlCommand("insert into blood_transfers(patient_name,hospital_name,blood_type,units) values(@patient,@hospital,@blood,@units)", db, tx);
        insert.Parameters.AddWithValue("patient", t.PatientName.Trim());
        insert.Parameters.AddWithValue("hospital", t.HospitalName.Trim());
        insert.Parameters.AddWithValue("blood", t.BloodType);
        insert.Parameters.AddWithValue("units", t.Units);
        await insert.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return Results.Ok(new { message = "Blood issued successfully." });
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
});

app.Run();

static LoginDto CleanLogin(LoginDto dto) => new(dto.Name.Trim(), dto.Password.Trim());
static bool ValidBlood(string blood) => new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" }.Contains(blood);
static string StockStatus(int units) => units <= 0 ? "Empty" : units < 5 ? "Low" : "Available";

static string NormalizeConnectionString(string value)
{
    if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return value;
    }

    var uri = new Uri(value);
    var userInfo = uri.UserInfo.Split(':', 2);
    var database = uri.AbsolutePath.TrimStart('/');
    return $"Host={uri.Host};Port={uri.Port};Database={database};Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : "")};SSL Mode=Require;Trust Server Certificate=true";
}

static string HashPassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
    return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
}

static bool VerifyPassword(string password, string stored)
{
    var parts = stored.Split(':');
    if (parts.Length != 2) return false;
    var salt = Convert.FromBase64String(parts[0]);
    var original = Convert.FromBase64String(parts[1]);
    var current = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
    return CryptographicOperations.FixedTimeEquals(current, original);
}

static async Task<int> ScalarInt(NpgsqlConnection db, string sql, params (string Name, object Value)[] parameters)
{
    await using var cmd = new NpgsqlCommand(sql, db);
    foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
}

static async Task<int> ScalarIntTx(NpgsqlConnection db, NpgsqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
{
    await using var cmd = new NpgsqlCommand(sql, db, tx);
    foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
}

static async Task<List<Dictionary<string, object?>>> Query(string db, string query)
{
    await using var conn = new NpgsqlConnection(db);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(query, conn);
    await using var reader = await cmd.ExecuteReaderAsync();
    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
        rows.Add(row);
    }
    return rows;
}

static async Task<IResult> Delete(string db, string table, int id)
{
    await using var conn = new NpgsqlConnection(db);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand($"delete from {table} where id=@id", conn);
    cmd.Parameters.AddWithValue("id", id);
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok();
}

static async Task<IResult> SaveDonor(string db, Donor d)
{
    if (string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Phone) || string.IsNullOrWhiteSpace(d.Gender) || !ValidBlood(d.BloodType) || d.Age is < 1 or > 120)
        return Results.BadRequest(new { error = "Check donor name, age, phone, gender, and blood type." });
    await using var conn = new NpgsqlConnection(db);
    await conn.OpenAsync();
    var sql = d.Id > 0
        ? "update donors set name=@name,age=@age,phone=@phone,gender=@gender,blood_type=@blood,address=@address where id=@id"
        : "insert into donors(name,age,phone,gender,blood_type,address) values(@name,@age,@phone,@gender,@blood,@address)";
    await using var cmd = new NpgsqlCommand(sql, conn);
    if (d.Id > 0) cmd.Parameters.AddWithValue("id", d.Id);
    cmd.Parameters.AddWithValue("name", d.Name.Trim());
    cmd.Parameters.AddWithValue("age", d.Age);
    cmd.Parameters.AddWithValue("phone", d.Phone.Trim());
    cmd.Parameters.AddWithValue("gender", d.Gender.Trim());
    cmd.Parameters.AddWithValue("blood", d.BloodType);
    cmd.Parameters.AddWithValue("address", d.Address?.Trim() ?? "");
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok();
}

static async Task<IResult> SaveStock(string db, Stock s)
{
    if (string.IsNullOrWhiteSpace(s.DonorName) || !ValidBlood(s.BloodType) || s.Units < 0)
        return Results.BadRequest(new { error = "Check donor name, blood type, and units." });
    await using var conn = new NpgsqlConnection(db);
    await conn.OpenAsync();
    var sql = s.Id > 0
        ? "update blood_stock set donor_name=@donor,blood_type=@blood,units=@units,status=@status where id=@id"
        : "insert into blood_stock(donor_name,blood_type,units,status) values(@donor,@blood,@units,@status)";
    await using var cmd = new NpgsqlCommand(sql, conn);
    if (s.Id > 0) cmd.Parameters.AddWithValue("id", s.Id);
    cmd.Parameters.AddWithValue("donor", s.DonorName.Trim());
    cmd.Parameters.AddWithValue("blood", s.BloodType);
    cmd.Parameters.AddWithValue("units", s.Units);
    cmd.Parameters.AddWithValue("status", StockStatus(s.Units));
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok();
}

static async Task<IResult> SaveRequest(string db, BloodRequest r)
{
    if (string.IsNullOrWhiteSpace(r.PatientName) || string.IsNullOrWhiteSpace(r.HospitalName) || !ValidBlood(r.BloodType) || r.Units <= 0)
        return Results.BadRequest(new { error = "Check patient, hospital, blood type, and units." });
    await using var conn = new NpgsqlConnection(db);
    await conn.OpenAsync();
    var sql = r.Id > 0
        ? "update blood_requests set patient_name=@patient,hospital_name=@hospital,blood_type=@blood,units=@units,status=@status where id=@id"
        : "insert into blood_requests(patient_name,hospital_name,blood_type,units,status) values(@patient,@hospital,@blood,@units,@status)";
    await using var cmd = new NpgsqlCommand(sql, conn);
    if (r.Id > 0) cmd.Parameters.AddWithValue("id", r.Id);
    cmd.Parameters.AddWithValue("patient", r.PatientName.Trim());
    cmd.Parameters.AddWithValue("hospital", r.HospitalName.Trim());
    cmd.Parameters.AddWithValue("blood", r.BloodType);
    cmd.Parameters.AddWithValue("units", r.Units);
    cmd.Parameters.AddWithValue("status", string.IsNullOrWhiteSpace(r.Status) ? "Pending" : r.Status.Trim());
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok();
}

record LoginDto(string Name, string Password);
record Donor(int Id, string Name, int Age, string Phone, string Gender, string BloodType, string? Address);
record Stock(int Id, string DonorName, string BloodType, int Units);
record BloodRequest(int Id, string PatientName, string HospitalName, string BloodType, int Units, string Status);
record Transfer(string PatientName, string HospitalName, string BloodType, int Units);
record TransferReport(int Id, string PatientName, string HospitalName, string BloodType, int Units, DateTimeOffset TransferredAt);
