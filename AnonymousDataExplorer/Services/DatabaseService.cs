using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using System.Data;
using System.Data.Common;

namespace AnonymousDataExplorer.Services
{
	public class DatabaseService
	{
		private readonly DbConnection _connection;
		private readonly DbProvider _provider;

		public DatabaseService(DbProvider provider, IConfiguration config)
		{
			_provider = provider;
			var connectionString = provider switch
			{
				DbProvider.SQLite => config.GetConnectionString("SqliteConnection")!,
				DbProvider.MSSQL => config.GetConnectionString("MssqlConnection")!,
				DbProvider.MariaDB => config.GetConnectionString("MariadbConnection")!,
				_ => throw new NotSupportedException()
			};

			_connection = provider switch
			{
				DbProvider.SQLite => new SqliteConnection(connectionString),
				DbProvider.MSSQL => new SqlConnection(connectionString),
				DbProvider.MariaDB => new MySqlConnection(connectionString),
				_ => throw new NotSupportedException()
			};
		}

		public async Task<List<string>> GetTableNamesAsync() // names of tables in DB for comboBox f.e.
		{
			var result = new List<string>();

			if (_connection.State != ConnectionState.Open)
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

		public async Task<List<string>> GetColumnNamesOnlyAsync(string tableName) // names of columns in table - for creating columns and fields
		{
			var result = new List<string>();
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();

			if (_provider == DbProvider.SQLite)
				cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
			else if (_provider == DbProvider.MSSQL)
				cmd.CommandText = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}';";
			else if (_provider == DbProvider.MariaDB)
				cmd.CommandText = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';";
			else
				throw new NotSupportedException();

			using var reader = await cmd.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				var name = _provider switch
				{
					DbProvider.SQLite => reader["name"].ToString(),
					DbProvider.MSSQL => reader["COLUMN_NAME"].ToString(),
					DbProvider.MariaDB => reader["COLUMN_NAME"].ToString(),
					_ => null
				}; // name of columns

				if (!string.IsNullOrEmpty(name))
					result.Add(name);
			}

			await _connection.CloseAsync();
			return result;
		}

		public async Task<string?> GetPrimaryKeyColumnAsync(string tableName) // returns name of column that is marked as PK
		{
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();
			using var cmd = _connection.CreateCommand();

			if (_provider == DbProvider.SQLite)
			{
				cmd.CommandText = $"PRAGMA table_info({Quote(tableName)});";
				using var reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync())
				{
					var isPk = Convert.ToInt32(reader["pk"]);
					if (isPk == 1)
						return reader["name"].ToString();
				}
			}
			else if (_provider == DbProvider.MSSQL)
			{
				cmd.CommandText = $@"
			SELECT COLUMN_NAME
			FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
			WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1
			AND TABLE_NAME = '{tableName}';";

				using var reader = await cmd.ExecuteReaderAsync();
				if (await reader.ReadAsync())
					return reader.GetString(0);
			}
			else if (_provider == DbProvider.MariaDB)
			{
				cmd.CommandText = $@"
			SELECT COLUMN_NAME
			FROM information_schema.COLUMNS
			WHERE TABLE_SCHEMA = DATABASE()
			AND TABLE_NAME = '{tableName}'
			AND COLUMN_KEY = 'PRI';";

				using var reader = await cmd.ExecuteReaderAsync();
				if (await reader.ReadAsync())
					return reader.GetString(0);
			}
			else throw new NotSupportedException();

			await _connection.CloseAsync();
			return null;
		}

		private string Quote(string identifier)
		{
			return _provider switch
			{
				DbProvider.MSSQL => $"[{identifier}]",
				DbProvider.MariaDB => $"`{identifier}`",
				DbProvider.SQLite => $"\"{identifier}\"",
				_ => identifier
			};
		}


		#region CRUD methods

		public async Task<List<Dictionary<string, object>>> GetDataRowsAsync(string tableName) // for loading and refresh
		{
			var rows = new List<Dictionary<string, object>>();
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			using var command = _connection.CreateCommand();

			if (_provider == DbProvider.SQLite)
				command.CommandText = $"SELECT * FROM \"{tableName}\"";
			else if (_provider == DbProvider.MSSQL)
				command.CommandText = $"SELECT * FROM [{tableName}]";
			else if (_provider == DbProvider.MariaDB)
				command.CommandText = $"SELECT * FROM `{tableName}`";
			else
				throw new NotSupportedException();

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

		public async Task InsertRowAsync(string tableName, string pkColumn, Dictionary<string, object> data)
		{
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			var insertable = data.Where(kvp => kvp.Key != pkColumn);

			var columns = string.Join(", ", insertable.Select(kvp => Quote(kvp.Key)));
			var parameters = string.Join(", ", insertable.Select(kvp => $"@{kvp.Key}"));

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"INSERT INTO {Quote(tableName)} ({columns}) VALUES ({parameters})";

			foreach (var kvp in insertable)
			{
				var param = cmd.CreateParameter();
				param.ParameterName = $"@{kvp.Key}";

				object value = kvp.Value;

				// obecné ošetření na DBNull
				if (value is string s && string.IsNullOrWhiteSpace(s))
					value = DBNull.Value;
				else if (value is DateTime dt && dt == default)
					value = DBNull.Value;
				else if (value == null)
					value = DBNull.Value;

				// konkrétní sloupce: fallback default hodnoty
				//if (value == DBNull.Value && kvp.Key.Equals("PurchasePrice", StringComparison.OrdinalIgnoreCase))
				//	value = 0;
				//if (value == DBNull.Value && kvp.Key.Equals("LastName", StringComparison.OrdinalIgnoreCase))
				//	value = "Unknown";
				if (value == DBNull.Value && kvp.Key.Equals("Birthday", StringComparison.OrdinalIgnoreCase))
					value = new DateTime(2000, 1, 1); // nebo co chceš

				param.Value = value;
				cmd.Parameters.Add(param);
			}

			await cmd.ExecuteNonQueryAsync();
			await _connection.CloseAsync();
		}

		public async Task DeleteRowAsync(string tableName, string pkColumn, object pkValue) // deleting row in table
		{
			if (_connection.State != ConnectionState.Open)
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
		
		#endregion
	}

}
