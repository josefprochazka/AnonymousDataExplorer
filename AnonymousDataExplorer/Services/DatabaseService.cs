using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using SQLitePCL;
using System.Data;
using System.Data.Common;

namespace AnonymousDataExplorer.Services
{
	public class DatabaseService
	{
		private readonly DbConnection _connection;
		private readonly DbProvider _provider;

		#region Constructor

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

		#endregion

		#region Methods for getting MetaData from DB

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

		public async Task<string?> GetPrimaryKeyColumnNameAsync(string tableName) // returns name of column that is marked as PK
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

		public async Task<Dictionary<string, (string DataType, bool IsNotNull)>> GetColumnMetaAsync(string tableName) // returns meta data for tables in DB
		{
			var result = new Dictionary<string, (string, bool)>();

			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();

			if (_provider == DbProvider.SQLite)
				cmd.CommandText = $"PRAGMA table_info({Quote(tableName)});";
			else if (_provider == DbProvider.MSSQL)
				cmd.CommandText = $@"
				SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
				FROM INFORMATION_SCHEMA.COLUMNS 
				WHERE TABLE_NAME = '{tableName}';";
			else if (_provider == DbProvider.MariaDB)
				cmd.CommandText = $@"
				SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
				FROM INFORMATION_SCHEMA.COLUMNS 
				WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}';";
			else
				throw new NotSupportedException();

			using var reader = await cmd.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				string? name = null;
				string? type = null;
				bool notNull = false;

				if (_provider == DbProvider.SQLite)
				{
					name = reader["name"].ToString();
					type = reader["type"].ToString();
					notNull = Convert.ToInt32(reader["notnull"]) == 1;
				}
				else
				{
					name = reader["COLUMN_NAME"].ToString();
					type = reader["DATA_TYPE"].ToString();
					notNull = reader["IS_NULLABLE"].ToString() == "NO";
				}

				if (!string.IsNullOrEmpty(name))
					result[name] = (type ?? "", notNull);
			}

			await _connection.CloseAsync();
			return result;
		}

		#endregion Methods for getting MetaData from DB

		#region CRUD methods

		public async Task<List<Dictionary<string, object>>> GetDataRowsAsync(string tableName) // for loading and refresh
		{
			var rows = new List<Dictionary<string, object>>();
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			using var command = _connection.CreateCommand();
			command.CommandText = $"SELECT * FROM {Quote(tableName)}";

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

		public async Task<Dictionary<string, object>> GetRowByIdAsync(string tableName, string pkColumn, object id) // when update or insert
		{
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			using var command = _connection.CreateCommand();
			command.CommandText = $"SELECT * FROM {Quote(tableName)} WHERE {Quote(pkColumn)} = @id";

			var param = command.CreateParameter();
			param.ParameterName = "@id";
			param.Value = id;
			command.Parameters.Add(param);

			using var reader = await command.ExecuteReaderAsync();

			Dictionary<string, object> row = null;
			if (await reader.ReadAsync())
			{
				row = new Dictionary<string, object>();
				for (int i = 0; i < reader.FieldCount; i++)
				{
					row[reader.GetName(i)] = reader.GetValue(i);
				}
			}

			await _connection.CloseAsync();
			return row;
		}

		public async Task UpdateRowAsync(string tableName, string keyColumn, object keyValue, Dictionary<string, object> data, Dictionary<string, (string, bool)> columnMeta) // editing row
		{
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			var setForQueryList = data.Where(kvp => kvp.Key != keyColumn).Select(kvp => $"{Quote(kvp.Key)} = @{kvp.Key}").ToArray();
			var setClause = string.Join(", ", setForQueryList);

			string quotedTable = Quote(tableName);
			string quotedKey = Quote(keyColumn);

			using var command = _connection.CreateCommand();
			command.CommandText = $"UPDATE {quotedTable} SET {setClause} WHERE {quotedKey} = @keyValue";

			foreach (var kvp in data.Where(kvp => kvp.Key != keyColumn))
			{
				var param = command.CreateParameter();
				param.ParameterName = $"@{kvp.Key}";

				var columnType = columnMeta[kvp.Key].Item1.ToLower();
				var rawValue = kvp.Value?.ToString()?.Trim();

				if (columnType.Contains("date") || columnType.Contains("time"))
				{
					if (DateTime.TryParse(kvp.Value?.ToString(), out var parsedDate))
						param.Value = parsedDate;
					else
						param.Value = DBNull.Value;
				}
				else if (columnType.Contains("bit") || columnType.Contains("bool"))
				{
					param.Value = (rawValue == "true" || rawValue == "1") ? 1 : 0;
				}
				else if (string.IsNullOrWhiteSpace(rawValue))
				{
					param.Value = DBNull.Value;
				}
				else if (columnType.Contains("int") && int.TryParse(rawValue, out var i))
				{
					param.Value = i;
				}
				else if ((columnType.Contains("decimal") || columnType.Contains("numeric") || columnType.Contains("float") || columnType.Contains("double") || columnType.Contains("real"))
						 && double.TryParse(rawValue, out var d))
				{
					param.Value = d;
				}
				else
				{
					param.Value = rawValue;
				}

				command.Parameters.Add(param);
			}

			var keyParam = command.CreateParameter();
			keyParam.ParameterName = "@keyValue";
			keyParam.Value = keyValue;
			command.Parameters.Add(keyParam);

			await command.ExecuteNonQueryAsync();
			await _connection.CloseAsync();
		}

		public async Task<object?> InsertRowAsync(string tableName, string pkColumn, Dictionary<string, object> data, Dictionary<string, (string, bool)> columnMeta) // inserting new row
		{
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			var dataForInsertableColumns = data.Where(kvp => kvp.Key != pkColumn);

			var columns = string.Join(", ", dataForInsertableColumns.Select(kvp => Quote(kvp.Key)));
			var parameters = string.Join(", ", dataForInsertableColumns.Select(kvp => $"@{kvp.Key}"));

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = $"{GetInsertQuery(tableName, columns, parameters)}";

			foreach (var kvp in dataForInsertableColumns)
			{
				var param = cmd.CreateParameter();
				param.ParameterName = $"@{kvp.Key}";

				object value = kvp.Value;

				// convert bool type to database value
				if (columnMeta.TryGetValue(kvp.Key, out var metaData))
				{
					var columnType = metaData.Item1.ToLower();
					if (columnType.Contains("bit") || columnType.Contains("bool"))
					{
						var valueToConvert = value?.ToString()?.ToLower();
						value = (valueToConvert == "true" || valueToConvert == "1") ? 1 : 0;
					}
				}

				// set defaul null values
				if (value is string s && string.IsNullOrWhiteSpace(s))
					value = DBNull.Value;
				else if (value is DateTime dt && dt == default)
					value = DBNull.Value;
				else if (value == null)
					value = DBNull.Value;

				param.Value = value;
				cmd.Parameters.Add(param);
			}

			object? newId = await cmd.ExecuteScalarAsync();
			await _connection.CloseAsync();

			return newId;
		}

		public async Task DeleteRowAsync(string tableName, string pkColumn, object pkValue) // deleting row
		{
			if (_connection.State != ConnectionState.Open)
				await _connection.OpenAsync();

			using var cmd = _connection.CreateCommand();

			var quotedTable = Quote(tableName);
			var quotedColumn = Quote(pkColumn);
			cmd.CommandText = $"DELETE FROM {quotedTable} WHERE {quotedColumn} = @id";

			var idParam = cmd.CreateParameter();
			idParam.ParameterName = "@id";
			idParam.Value = pkValue;
			cmd.Parameters.Add(idParam);

			await cmd.ExecuteNonQueryAsync();
			await _connection.CloseAsync();
		}

		#endregion

		#region Helper methods

		private string Quote(string identifier) // set special quotes for each database type
		{
			return _provider switch
			{
				DbProvider.MSSQL => $"[{identifier}]",
				DbProvider.MariaDB => $"`{identifier}`",
				DbProvider.SQLite => $"\"{identifier}\"",
				_ => identifier
			};
		}

		private string GetInsertQuery(string tableName, string columns, string parameters) // get universal INSERT query for each databaze type
		{
			var quotedTable = Quote(tableName);
			return _provider switch
			{
				DbProvider.SQLite => $"INSERT INTO {quotedTable} ({columns}) VALUES ({parameters}); SELECT last_insert_rowid();",
				DbProvider.MSSQL => $"INSERT INTO {quotedTable} ({columns}) OUTPUT INSERTED.{Quote("ID")} VALUES ({parameters});",
				DbProvider.MariaDB => $"INSERT INTO {quotedTable} ({columns}) VALUES ({parameters}); SELECT LAST_INSERT_ID();",
				_ => throw new NotSupportedException("Unsupported DB type")
			};
		}

		#endregion
	}

}
