using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace AnonymousDataExplorer.Services
{
	public class AppDbContext : DbContext
	{
		private readonly DbProvider _provider;
		private readonly string _connectionString;

		public AppDbContext(DbProvider provider, IConfiguration config)
		{
			_provider = provider;
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
		private readonly DbProvider _provider;
		private readonly DbConnection _connection;

		public DatabaseService(AppDbContext context, DbProvider provider)
		{
			_context = context;
			_provider = provider;
			_connection = _context.Database.GetDbConnection();
		}

		public async Task<List<string>> GetTableNamesAsync()
		{
			if (_provider != DbProvider.SQLite)
				throw new NotImplementedException();

			var result = new List<string>();

			await _connection.OpenAsync();
			using var command = _connection.CreateCommand();
			command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";

			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				result.Add(reader.GetString(0));
			}

			await _connection.CloseAsync();
			return result;
		}

		public async Task<List<Dictionary<string, object>>> GetDataRowsAsync(string tableName)
		{
			var rows = new List<Dictionary<string, object>>();
			await _connection.OpenAsync();

			using var command = _connection.CreateCommand();
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

			await _connection.CloseAsync();
			return rows;
		}

		public async Task<List<string>> GetColumnNamesOnlyAsync(string tableName)
		{
			var result = new List<string>();
			await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});";

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var name = reader["name"].ToString();
				if (!string.IsNullOrEmpty(name))
					result.Add(name);
			}

			await _connection.CloseAsync();
			return result;
		}

		public async Task UpdateRowAsync(string tableName, string keyColumn, object keyValue, Dictionary<string, object> data)
		{
			await _connection.OpenAsync();

			var setParts = data
				.Where(kvp => kvp.Key != keyColumn)
				.Select(kvp => $"{kvp.Key} = @{kvp.Key}").ToArray();

			var setClause = string.Join(", ", setParts);

			using var command = _connection.CreateCommand();
			command.CommandText = $"UPDATE [{tableName}] SET {setClause} WHERE {keyColumn} = @keyValue";

			foreach (var kvp in data.Where(kvp => kvp.Key != keyColumn))
			{
				var param = command.CreateParameter();
				param.ParameterName = $"@{kvp.Key}";
				param.Value = kvp.Value ?? DBNull.Value;
				command.Parameters.Add(param);
			}

			var keyParam = command.CreateParameter();
			keyParam.ParameterName = "@keyValue";
			keyParam.Value = keyValue;
			command.Parameters.Add(keyParam);

			await command.ExecuteNonQueryAsync();
			await _connection.CloseAsync();
		}

		public async Task<string?> GetPrimaryKeyColumnAsync(string tableName)
		{
			await _connection.OpenAsync();
			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});";

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var isPk = Convert.ToInt32(reader["pk"]);
				if (isPk == 1)
					return reader["name"].ToString();
			}

			await _connection.CloseAsync();
			return null;
		}

		public async Task DeleteRowAsync(string tableName, string pkColumn, object pkValue)
		{
			await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"DELETE FROM [{tableName}] WHERE [{pkColumn}] = @id";

			var idParam = cmd.CreateParameter();
			idParam.ParameterName = "@id";
			idParam.Value = pkValue;
			cmd.Parameters.Add(idParam);

			await cmd.ExecuteNonQueryAsync();
			await _connection.CloseAsync();
		}

		public async Task InsertRowAsync(string tableName, string pkColumn, Dictionary<string, object> data)
		{
			await _connection.OpenAsync();

			var insertable = data.Where(kvp => kvp.Key != pkColumn);

			var columns = string.Join(", ", insertable.Select(kvp => $"[{kvp.Key}]"));
			var parameters = string.Join(", ", insertable.Select(kvp => $"@{kvp.Key}"));

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"INSERT INTO [{tableName}] ({columns}) VALUES ({parameters})";

			foreach (var kvp in insertable)
			{
				var param = cmd.CreateParameter();
				param.ParameterName = $"@{kvp.Key}";
				param.Value = kvp.Value ?? DBNull.Value;
				cmd.Parameters.Add(param);
			}

			await cmd.ExecuteNonQueryAsync();
			await _connection.CloseAsync();
		}
	}

}
