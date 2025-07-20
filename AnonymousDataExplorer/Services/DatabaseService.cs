using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnonymousDataExplorer.Services
{
	public class AppDbContext : DbContext
	{
		private readonly DbProvider _provider;
		private readonly string _connectionString;

		public AppDbContext(IConfiguration config)
		{
			_provider = DbProvider.SQLite;
			//OnConfiguring(new DbContextOptionsBuilder());
			_connectionString = config.GetConnectionString("DefaultConnection");
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			switch (_provider)
			{
				case DbProvider.SQLite:
					optionsBuilder.UseSqlite(_connectionString);
					break;
				case DbProvider.MSSQL:
					throw new NotImplementedException("MSSQL provider not implemented yet");
				case DbProvider.MariaDB:
					throw new NotImplementedException("MariaDB provider not implemented yet");
				default:
					throw new NotSupportedException();
			}
		}
	}

	public class DatabaseService
	{
		private readonly AppDbContext _context;
		private readonly string _connectionString;

		public DatabaseService(AppDbContext context, IConfiguration configuration)
		{
			_context = context;
			_connectionString = configuration.GetConnectionString("DefaultConnection");
		}

		public async Task<List<string>> GetTableNamesAsync()
		{
			var result = new List<string>();

			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();

			var command = new SqliteCommand(
				"SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';",
				connection);

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				result.Add(reader.GetString(0));
			}

			return result;
		}

		public async Task<List<Dictionary<string, object>>> GetDataRowsAsync(string tableName)
		{
			var rows = new List<Dictionary<string, object>>();

			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();

			var command = connection.CreateCommand();
			command.CommandText = $"SELECT * FROM [{tableName}]";

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var row = new Dictionary<string, object>();
				for (int i = 0; i < reader.FieldCount; i++)
				{
					row[reader.GetName(i)] = reader.GetValue(i);
				}
				rows.Add(row);
			}

			return rows;
		}

		public async Task<List<string>> GetColumnNamesOnlyAsync(string tableName)
		{
			var result = new List<string>();

			using var conn = new SqliteConnection(_connectionString);
			await conn.OpenAsync();

			using var cmd = conn.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});";

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var name = reader["name"].ToString();
				if (!string.IsNullOrEmpty(name))
					result.Add(name);
			}

			return result;
		}

		public async Task UpdateRowAsync(string tableName, string keyColumn, object keyValue, Dictionary<string, object> data)
		{
			using var connection = new SqliteConnection(_connectionString);
			await connection.OpenAsync();

			var setParts = data
				.Where(kvp => kvp.Key != keyColumn)
				.Select(kvp => $"{kvp.Key} = @{kvp.Key}").ToArray();

			var setClause = string.Join(", ", setParts);

			var command = connection.CreateCommand();
			command.CommandText = $"UPDATE [{tableName}] SET {setClause} WHERE {keyColumn} = @keyValue";

			foreach (var kvp in data.Where(kvp => kvp.Key != keyColumn))
			{
				command.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
			}

			command.Parameters.AddWithValue("@keyValue", keyValue);

			await command.ExecuteNonQueryAsync();
		}

		public async Task<string?> GetPrimaryKeyColumnAsync(string tableName)
		{
			using var conn = new SqliteConnection(_connectionString);
			await conn.OpenAsync();

			var cmd = conn.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});";

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var isPk = reader.GetInt32(reader.GetOrdinal("pk"));
				if (isPk == 1)
					return reader.GetString(reader.GetOrdinal("name")); // název PK sloupce
			}

			return null;
		}

		public async Task DeleteRowAsync(string tableName, string pkColumn, object pkValue)
		{
			using var conn = new SqliteConnection(_connectionString);
			await conn.OpenAsync();

			var cmd = conn.CreateCommand();
			cmd.CommandText = $"DELETE FROM [{tableName}] WHERE [{pkColumn}] = @id";
			cmd.Parameters.AddWithValue("@id", pkValue);

			await cmd.ExecuteNonQueryAsync();
		}

		public async Task InsertRowAsync(string tableName, string pkColumn, Dictionary<string, object> data)
		{
			using var conn = new SqliteConnection(_connectionString);
			await conn.OpenAsync();

			var insertable = data.Where(kvp => kvp.Key != pkColumn);

			var columns = string.Join(", ", insertable.Select(kvp => $"[{kvp.Key}]"));
			var parameters = string.Join(", ", insertable.Select(kvp => $"@{kvp.Key}"));

			var cmd = conn.CreateCommand();
			cmd.CommandText = $"INSERT INTO [{tableName}] ({columns}) VALUES ({parameters})";

			foreach (var kvp in insertable)
			{
				cmd.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
			}

			await cmd.ExecuteNonQueryAsync();
		}

	}
}
