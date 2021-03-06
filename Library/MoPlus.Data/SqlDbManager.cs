﻿/*<copyright>
Mo+ Solution Builder is a model oriented programming language and IDE, used for building models and generating code and other documents in a model driven development process.

Copyright (C) 2013 Dave Clemmer, Intelligent Coding Solutions, LLC

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this program.  If not, see <http://www.gnu.org/licenses/>.
</copyright>*/
using System;
using System.Linq;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Security.Permissions;
using System.Xml;
using Ent = Microsoft.Practices.EnterpriseLibrary.Data;
using MoPlus.Common;

namespace MoPlus.Data
{
	///--------------------------------------------------------------------------------
	/// <summary>This class is used for managing operations with an instance of a sql
	/// database.  All connections and transactions are managed internally.  Following is
	/// a typical workflow for using the sql manager:</summary>
	///--------------------------------------------------------------------------------
	public class SqlDbManager
	{
		#region "Fields and Properties"
		// to hold the sql database
		protected Ent.Sql.SqlDatabase _database = null;

		protected DatabaseOptions _dbOptions = null;
		///--------------------------------------------------------------------------------
		/// <summary>This property gets the database options.</summary>
		///--------------------------------------------------------------------------------
		public DatabaseOptions DBOptions
		{
			get
			{
				return _dbOptions;
			}
		}

		protected DbConnection _connection = null;
		///--------------------------------------------------------------------------------
		/// <summary>This property gets/creates the database connection.</summary>
		///--------------------------------------------------------------------------------
		public DbConnection Connection
		{
			get
			{
				if (_connection == null)
				{
					_connection = _database.CreateConnection();
				}
				return _connection;
			}
		}

		protected DbTransaction _transaction = null;
		///--------------------------------------------------------------------------------
		/// <summary>This property gets/creates the database transaction.</summary>
		///--------------------------------------------------------------------------------
		public DbTransaction Transaction
		{
			get
			{
				if (_transaction == null)
				{
					_transaction = Connection.BeginTransaction();
				}
				return _transaction;
			}
		}

		// TODO: test for performance, NameObjectCollection may be faster
		protected static GenericEntity _dbSprocCache = null;
		///--------------------------------------------------------------------------------
		/// <summary>This property gets/creates/caches the database stored procedures in generic
		/// entity format.</summary>
		///--------------------------------------------------------------------------------
		protected GenericEntity DbProcs
		{
			get
			{
				if (_dbSprocCache == null)
				{
					_dbSprocCache = new GenericEntity();
					_dbSprocCache.EntityName = "Stored Procedures";
				}
				if (_dbSprocCache.MethodList == null)
				{
					_dbSprocCache.MethodList = new SortableDataObjectList<GenericMethod>();
				}
				return _dbSprocCache;
			}
			set
			{
				_dbSprocCache = value;
			}
		}

		#endregion "Fields and Properties"

		#region "Methods"
		///--------------------------------------------------------------------------------
		/// <summary>This method commits the transaction.  The connection is also closed.</summary>
		///--------------------------------------------------------------------------------
		public void Commit()
		{
			if (_transaction != null)
			{
				_transaction.Commit();
			}
			Close();
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method rolls back the transaction.  The connection is also closed.</summary>
		///--------------------------------------------------------------------------------
		public void Rollback()
		{
			if (_transaction != null)
			{
				_transaction.Rollback();
			}
			Close();
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method closes the connection.</summary>
		///--------------------------------------------------------------------------------
		public void Close()
		{
			if (_connection != null)
			{
				_connection.Close();
			}
			_transaction = null;
			_connection = null;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method gets a sproc from the database and loads it into a generic
		/// method.  The results can be added to the stored procedure cache, if specified.</summary>
		/// 
		/// <param name="sprocName">Name of the stored procedure to get.</param>
		/// <param name="addSprocToCache">Flag indicating whether to add sproc method to cache.</param>
		/// 
		/// <returns>A stored procedure from the db as a GenericMethod.</returns>
		///--------------------------------------------------------------------------------
		public GenericMethod GetSprocFromDB(string sprocName, bool addSprocToCache)
		{
			GenericMethod sprocMethod = null;

			try
			{
				// go to the database to retrieve the sproc
				DbCommand command = _database.GetStoredProcCommand("sp_sproc_columns");
				string sprocShortName = sprocName.Replace("[", "").Replace("]", "");
				if (sprocShortName.IndexOf('.') > 0)
				{
					sprocShortName = sprocShortName.Substring(sprocShortName.IndexOf('.')+1);
				}
				_database.AddInParameter(command, "@procedure_name", DbType.String, sprocShortName);
				command.Connection = Connection;
				command.CommandTimeout = DBOptions.CommandTimeout;
				IDataReader dbSproc = _database.ExecuteReader(command);
				bool foundRows = false;
				// add sproc parameters to method
				while (dbSproc.Read())
				{
					if (foundRows == false)
					{
						// create the method with sproc name
						sprocMethod = new GenericMethod();
						sprocMethod.MethodName = sprocName;
						sprocMethod.MethodParameterList = new SortableDataObjectList<GenericParameter>();
						foundRows = true;
					}
					GenericParameter sprocParameter = new GenericParameter();
					sprocParameter.ParameterName = dbSproc["COLUMN_NAME"].GetString();
					if (sprocParameter.ParameterName.StartsWith("@") == true)
					{
						sprocParameter.ParameterName = sprocParameter.ParameterName.Substring(1);
					}
					sprocParameter.ParameterTypeCode = dbSproc["COLUMN_TYPE"].GetInt();
					sprocParameter.DataTypeCode = dbSproc["DATA_TYPE"].GetInt();
					sprocParameter.DataTypeName = dbSproc["TYPE_NAME"].GetString();
					sprocParameter.Precision = dbSproc["PRECISION"].GetInt();
					sprocParameter.Size = dbSproc["LENGTH"].GetInt();
					sprocParameter.IsNullable = dbSproc["NULLABLE"].GetBool();
					sprocParameter.Order = dbSproc["ORDINAL_POSITION"].GetInt();
					sprocMethod.MethodParameterList.Add(sprocParameter);
				}
				dbSproc.Close();

				if (foundRows == true)
				{
					if (addSprocToCache == true)
					{
						// add sproc to the cache
						DbProcs.MethodList.Add(sprocMethod);
					}
				}
				else
				{
					// sproc not found, throw exception
					throw new System.Exception("MoPlus.Data.BuildSprocParamsWithInputValues - stored procedure" + sprocName + " not found in database.");
				}
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}

			return sprocMethod;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method sets up a stored procedure command, loaded with values from
		/// the input data access object where appropriate.</summary>
		/// 
		/// <param name="sprocName">Name of the stored procedure.</param>
		/// <param name="sprocParams">The input stored procedure parameter values.</param>
		/// 
		/// <returns>A DbCommand for the stored procedure.</returns>
		///--------------------------------------------------------------------------------
		public DbCommand SetupSprocCommand(string sprocName, NameObjectCollection sprocParams)
		{
			GenericMethod sprocMethod = null;
			DbCommand command = null;

			try
			{
				// get sproc method from cache or db
				sprocMethod = DbProcs.MethodList.Find("MethodName", sprocName);
				if (sprocMethod == null)
				{
					// get sproc from db an add to cache
					sprocMethod = GetSprocFromDB(sprocName, true);
				}

				// set up the base command information
				command = _database.GetStoredProcCommand(sprocName);
				command.Connection = Connection;
				command.CommandTimeout = DBOptions.CommandTimeout;

				// load the sproc parameters with input data if found
				foreach (GenericParameter loopParameter in sprocMethod.MethodParameterList)
				{
					bool foundValue = false;
					if (sprocParams[loopParameter.ParameterName] != null && sprocParams[loopParameter.ParameterName].GetString() != String.Empty)
					{
						foundValue = true;
					}
					if (foundValue == false && loopParameter.IsNullable == false && loopParameter.ParameterTypeCode == 1)
					{
						// required parameter not found, throw exception
						throw new System.Exception("MoPlus.Data.BuildSprocParamsWithInputValues - required input value parameter " + loopParameter.ParameterName + " for stored procedure " + sprocName + " not found.");
					}
					_database.AddParameter(command, loopParameter.ParameterName, SqlHelper.GetDbTypeFromSqlDataType(loopParameter.DataTypeCode), loopParameter.Size, SqlHelper.GetParameterDirectionFromColumnType(loopParameter.ParameterTypeCode), loopParameter.IsNullable, (byte)loopParameter.Precision, (byte)4, loopParameter.ParameterName, DataRowVersion.Default, sprocParams[loopParameter.ParameterName]);
				}
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}
			return command;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method executes a stored procedure command using a data reader
		/// (to be used for get operations.  The connection should be closed by the user.</summary>
		/// 
		/// <param name="sprocName">Name of the stored procedure.</param>
		/// <param name="sprocParams">The input stored procedure parameter values.</param>
		/// 
		/// <returns>A IDataReader with query results.</returns>
		///--------------------------------------------------------------------------------
		public DbDataReader ExecuteReader(string sprocName, NameObjectCollection sprocParams)
		{
			DbCommand command = null;
			return ExecuteReader(sprocName, sprocParams, out command);
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method executes a stored procedure command using a data reader
		/// (to be used for get operations.  The connection should be closed by the user
		/// and ouput parameters read after the connection is closed.</summary>
		/// 
		/// <param name="sprocName">Name of the stored procedure.</param>
		/// <param name="sprocParams">The input stored procedure parameter values.</param>
		/// <param name="command">The output command (to get output parameters).</param>
		/// 
		/// <returns>A IDataReader with query results.</returns>
		///--------------------------------------------------------------------------------
		public DbDataReader ExecuteReader(string sprocName, NameObjectCollection sprocParams, out DbCommand command)
		{
			command = null;
			DbDataReader reader = null;

			try
			{
				// build the command and execute the reader for get operations
				command = SetupSprocCommand(sprocName, sprocParams);
				reader = (DbDataReader)_database.ExecuteReader(command);

				// put output/return values into sproc params
				foreach (DbParameter loopParameter in command.Parameters)
				{
					sprocParams[SqlHelper.GetParameterNameFromSqlParameterName(loopParameter.ParameterName)] = loopParameter.Value;
				}
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}
			return reader;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method executes a sql statement using a data reader (to be used
		/// for get operations.  The connection should be closed by the user.</summary>
		/// 
		/// <param name="sqlStatement">The Sql statement to be executed.</param>
		/// 
		/// <returns>A IDataReader with query results.</returns>
		///--------------------------------------------------------------------------------
		public DbDataReader ExecuteRawSqlReader(string sqlStatement)
		{
			DbCommand command = null;
			DbDataReader reader = null;

			try
			{
				// build the command and execute the reader for get operations
				command = _database.GetSqlStringCommand(sqlStatement);
				command.Connection = Connection;
				command.CommandTimeout = DBOptions.CommandTimeout;
				reader = (DbDataReader)_database.ExecuteReader(command);
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}
			return reader;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method executes a non query (for database update operations).
		/// The user is expect to commit or roll back the transaction after one or more of
		/// these operations.</summary>
		/// 
		/// <param name="sprocName">Name of the stored procedure.</param>
		/// <param name="sprocParams">The input stored procedure parameter values.</param>
		/// 
		/// <returns>The result code of the operation.</returns>
		///--------------------------------------------------------------------------------
		public int ExecuteNonQuery(string sprocName, NameObjectCollection sprocParams)
		{
			DbCommand command = null;
			int results = DefaultValue.Int;

			try
			{
				// build the command and execute the non query for update operations
				command = SetupSprocCommand(sprocName, sprocParams);
				if (command.Connection.State == ConnectionState.Closed)
				{
					command.Connection.Open();
				}
				results = _database.ExecuteNonQuery(command, Transaction);

				// put output/return values into sproc params
				foreach (DbParameter loopParameter in command.Parameters)
				{
					sprocParams[SqlHelper.GetParameterNameFromSqlParameterName(loopParameter.ParameterName)] = loopParameter.Value;
				}
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}
			return results;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method executes the command and returns the first column of the
		/// first row of the results set.  All other columns and rows are ignored.</summary>
		/// 
		/// <param name="sprocName">Name of the stored procedure.</param>
		/// <param name="sprocParams">The input stored procedure parameter values.</param>
		/// 
		/// <returns>The results of the operation.</returns>
		///--------------------------------------------------------------------------------
		public object ExecuteScalar(string sprocName, NameObjectCollection sprocParams)
		{
			DbCommand command = null;
			object scalar = null;

			try
			{
				// build the command and execute scalar for simple results
				command = SetupSprocCommand(sprocName, sprocParams);
				scalar = _database.ExecuteScalar(command);

				// put output/return values into sproc params
				foreach (DbParameter loopParameter in command.Parameters)
				{
					sprocParams[SqlHelper.GetParameterNameFromSqlParameterName(loopParameter.ParameterName)] = loopParameter.Value;
				}
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}
			return scalar;
		}

		///--------------------------------------------------------------------------------
		/// <summary>This method closes the connection and populates the output parameters
		/// in the collection provided.</summary>
		/// 
		/// <remarks>This version of the Microsoft Enterprise library requires the connection
		/// to be closed in order to get output parameters after using ExecuteReader.</remarks>
		/// 
		/// <param name="command">The output command (to get output parameters).</param>
		/// <param name="sprocParams">The associated stored procedure parameter values.</param>
		/// 
		/// <returns>The result code of the operation.</returns>
		///--------------------------------------------------------------------------------
		public void CloseConnectionAndPopulateOutputParameters(DbCommand command, NameObjectCollection sprocParams)
		{
			try
			{
				if (command.Connection.State == ConnectionState.Open)
				{
					command.Connection.Close();
				}

				// put output/return values into sproc params
				foreach (DbParameter loopParameter in command.Parameters)
				{
					sprocParams[SqlHelper.GetParameterNameFromSqlParameterName(loopParameter.ParameterName)] = loopParameter.Value;
				}
			}
			catch (Exception ex)
			{
				bool reThrow = ExceptionHandler.HandleException(ex);
				if (reThrow) throw;
			}
		}

		#endregion "Methods"

		#region "Constructors"
		///--------------------------------------------------------------------------------
		/// <summary>This constructor creates the sql manager with an underlying Microsoft
		/// Enterprise Library sql database instance.</summary>
		/// 
		///	<param name="dbOptions">The database options for connecting to the database and using it.</param>
		///--------------------------------------------------------------------------------
		public SqlDbManager(DatabaseOptions dbOptions)
		{
			if (dbOptions == null || string.IsNullOrEmpty(dbOptions.ConnectionString))
			{
				throw new ArgumentException("MoPlus.Data.SqlManager - dbOptions");
			}
			_dbOptions = dbOptions;
			_database = new Ent.Sql.SqlDatabase(dbOptions.ConnectionString);
		}

		///--------------------------------------------------------------------------------
		/// <summary>This finalizer cleans stuff up.</summary>
		///--------------------------------------------------------------------------------
		~SqlDbManager()
		{
			// make sure connection and transaction are closed and cleaned up
			// TODO: test for performance, cleaning up here could have a performance impact
			Close();
			_database = null;
			_dbOptions = null;
		}

		#endregion "Constructors"
	}
}
