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
			_connectionString = provider switch
			{
				DbProvider.SQLite => config.GetConnectionString("SqliteConnection"),
				DbProvider.MSSQL => config.GetConnectionString("MssqlConnection"),
				DbProvider.MariaDB => config.GetConnectionString("MariadbConnection"),
				_ => throw new NotSupportedException()
			}; // default connectionString from json
		}

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) // calling in DbContext ctor
		{
			switch (_provider)
			{
				case DbProvider.SQLite:
					optionsBuilder.UseSqlite(_connectionString);
					break;
				case DbProvider.MSSQL:
					optionsBuilder.UseSqlServer(_connectionString);
					break;
				case DbProvider.MariaDB:
					optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString));
					break;
				default:
					throw new NotSupportedException();
			}
		}
	}

	public class DatabaseService
	{
		private readonly AppDbContext _context; // EF
		private readonly DbProvider _provider; // provider for showing structure
		private readonly DbConnection _connection; // for anonymous connect to DB

		public DatabaseService(AppDbContext context, DbProvider provider)
		{
			_context = context;
			_provider = provider;
			_connection = _context.Database.GetDbConnection();
		}

		public async Task<List<string>> GetTableNamesAsync() // names of tables in DB for comboBox f.e.
		{
			var result = new List<string>();

			await _connection.OpenAsync();
			using var command = _connection.CreateCommand();

			if (_provider == DbProvider.SQLite)
				command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
			else if (_provider == DbProvider.MSSQL)
				command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';";
			else if (_provider == DbProvider.MariaDB)
				command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE();";
			else
				throw new NotSupportedException();
			
			using var reader = await command.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				result.Add(reader.GetString(0));
			}

			await _connection.CloseAsync();
			return result;
		}

		public async Task<List<Dictionary<string, object>>> GetDataRowsAsync(string tableName) // for loading and refresh
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

		public async Task<List<string>> GetColumnNamesOnlyAsync(string tableName) // names of columns in table - for creating columns and fields
		{
			var result = new List<string>();
			await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});"; // query for metadata of table - table describing (columns etc.)

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var name = reader["name"].ToString(); // name of columns
				if (!string.IsNullOrEmpty(name))
					result.Add(name);
			}

			await _connection.CloseAsync();
			return result;
		}

		public async Task UpdateRowAsync(string tableName, string keyColumn, object keyValue, Dictionary<string, object> data) // update of row in table
		{
			await _connection.OpenAsync();

			// keyColumn = column with ID
			// keyValue = value of this ID
			var setParts = data.Where(kvp => kvp.Key != keyColumn).Select(kvp => $"{kvp.Key} = @{kvp.Key}").ToArray(); // query text for each SET
			var setClause = string.Join(", ", setParts); // all fields for SET

			using var command = _connection.CreateCommand();
			command.CommandText = $"UPDATE [{tableName}] SET {setClause} WHERE {keyColumn} = @keyValue";

			// setting how to run SET exactly (not for PK)
			foreach (var kvp in data.Where(kvp => kvp.Key != keyColumn))
			{
				var param = command.CreateParameter();
				param.ParameterName = $"@{kvp.Key}";
				param.Value = kvp.Value ?? DBNull.Value;
				command.Parameters.Add(param);
			}

			// setting params for PK (took from params in this method)
			var keyParam = command.CreateParameter();
			keyParam.ParameterName = "@keyValue";
			keyParam.Value = keyValue;
			command.Parameters.Add(keyParam);

			await command.ExecuteNonQueryAsync(); // executing update command
			await _connection.CloseAsync();
		}

		public async Task<string?> GetPrimaryKeyColumnAsync(string tableName) // returns name of column that is marked as PK
		{
			await _connection.OpenAsync();
			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"PRAGMA table_info({tableName});"; // query for metadata of table - table describing (columns etc.)

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync()) // finding "PK" column
			{
				var isPk = Convert.ToInt32(reader["pk"]);
				if (isPk == 1)
					return reader["name"].ToString(); // name of PK column
			}

			await _connection.CloseAsync();
			return null;
		}

		public async Task DeleteRowAsync(string tableName, string pkColumn, object pkValue) // deleting row in table
		{
			await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"DELETE FROM [{tableName}] WHERE [{pkColumn}] = @id";

			var idParam = cmd.CreateParameter();
			idParam.ParameterName = "@id";
			idParam.Value = pkValue;
			cmd.Parameters.Add(idParam);

			await cmd.ExecuteNonQueryAsync(); // executing delete command
			await _connection.CloseAsync();
		}

		public async Task InsertRowAsync(string tableName, string pkColumn, Dictionary<string, object> data) // inserting new row in table
		{
			await _connection.OpenAsync();

			var insertable = data.Where(kvp => kvp.Key != pkColumn); // only not PK columns

			var columns = string.Join(", ", insertable.Select(kvp => $"[{kvp.Key}]"));
			var parameters = string.Join(", ", insertable.Select(kvp => $"@{kvp.Key}"));

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"INSERT INTO [{tableName}] ({columns}) VALUES ({parameters})";

			foreach (var kvp in insertable) // same as update
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
