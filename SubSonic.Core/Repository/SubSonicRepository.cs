﻿// 
//   SubSonic - http://subsonicproject.com
// 
//   The contents of this file are subject to the New BSD
//   License (the "License"); you may not use this file
//   except in compliance with the License. You may obtain a copy of
//   the License at http://www.opensource.org/licenses/bsd-license.php
//  
//   Software distributed under the License is distributed on an 
//   "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either express or
//   implied. See the License for the specific language governing
//   rights and limitations under the License.
// 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using SubSonic.Extensions;
using SubSonic.DataProviders;
using SubSonic.Query;
using SubSonic.Schema;

namespace SubSonic.Repository
{
    /// <summary>
    /// A Repository class which wraps the a Database by type
    /// </summary>
    public class SubSonicRepository<T> : IRepository<T> where T : class, new()
    {
        private readonly IQuerySurface _db;

        public SubSonicRepository(IQuerySurface db)
        {
            _db = db;
        }


        #region IRepository<T> Members

        public ITable GetTable()
        {
            ITable tbl = _db.FindTable(typeof(T).Name);
            return tbl;
        }

        /// <summary>
        /// Loads a T object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item">The item.</param>
        /// <param name="column">The column.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        public bool Load(T item, string column, object value)
        {
            var qry = _db.Select.From(GetTable()).Where(column).IsEqualTo(value);
            return LoadQuery(item, qry);
        }

        

        /// <summary>
        /// Loads a T object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item">The item.</param>
        /// <param name="expression">The expression.</param>
        /// <returns></returns>
        public bool Load(T item, Expression<Func<T, bool>> expression)
        {
            var qry = _db.Select.From(GetTable()).Where(expression);
            return LoadQuery(item, qry);
        }

        /// <summary>
        /// Loads a query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item">The item. </param>
        /// <param name="qry">The SQLQuery to be loaded</param>
        /// <returns>Whether the operation is successful.</returns>
        private static bool LoadQuery(T item, SqlQuery qry) 
        {
            using (var rdr = qry.ExecuteReader())
            {
                if (rdr.Read())
                {
                    rdr.Load(item);
                    return true;
                }
                rdr.Dispose(); //Since it's in a "using" block, do we really need a Dispose here?
            }
            return false;
        }

        /// <summary>
        /// Returns all T items 
        /// </summary>
        public IQueryable<T> GetAll()
        {
            return _db.GetQuery<T>();
        }

        /// <summary>
        /// Returns a single record 
        /// </summary>
        public T GetByKey(object key)
        {
            ITable tbl = GetTable();
            return _db.Select.From(tbl)
                .Where(tbl.PrimaryKey.Name).IsEqualTo(key)
                .ExecuteSingle<T>();
        }

        /// <summary>
        /// Returns a server-side Paged List 
        /// </summary>
        public PagedList<T> GetPaged<TKey>(Func<T, TKey> orderBy, int pageIndex, int pageSize)
        {
            return new PagedList<T>(_db.GetQuery<T>().OrderBy(orderBy).AsQueryable(), pageIndex, pageSize);
        }

        /// <summary>
        /// Returns a server-side Paged List 
        /// </summary>
        public PagedList<T> GetPaged(int pageIndex, int pageSize)
        {
            ITable tbl = GetTable();
            string orderBy = tbl.PrimaryKey != null ? tbl.PrimaryKey.Name : tbl.Columns[0].Name;
            return GetPaged(orderBy, pageIndex, pageSize);
        }

        /// <summary>
        /// Returns a server-side Paged List 
        /// </summary>
        public PagedList<T> GetPaged(string sortBy, int pageIndex, int pageSize)
        {
            int totalCount = _db.Select.From<T>().GetRecordCount(); //this.GetAll().Count();
            ITable tbl = GetTable();

            var qry = _db.Select.From(tbl)
                .Paged(pageIndex, pageSize);

            if(!sortBy.EndsWith(" desc", StringComparison.InvariantCultureIgnoreCase))
                qry.OrderAsc(sortBy);
            else
                qry.OrderDesc(sortBy.Replace(" desc", ""));

            var list = qry.ExecuteTypedList<T>();

            PagedList<T> result = new PagedList<T>(list, totalCount, pageIndex, pageSize);

            //pull the page count
            return result;
        }

        /// <summary>
        /// Returns an IQueryable  based on the passed-in Expression  Chinook Database
        /// </summary>
        public IList<T> Search(string column, string value)
        {
            if(!value.EndsWith("%"))
                value += "%";
            var qry = _db.Select.From<T>().Where(column).Like(value).OrderAsc(column);
            return qry.ExecuteTypedList<T>();
        }

        /// <summary>
        /// Returns an IQueryable  based on the passed-in Expression  Chinook Database
        /// </summary>
        public IQueryable<T> Find(Expression<Func<T, bool>> expression)
        {
            return GetAll().Where(expression);
        }

                /// <summary>
        /// Adds a T item to the db
        /// </summary>
        public object Add(T item) {
            return Add(item, _db.Provider);
        }


        /// <summary>
        /// Adds a T item to the db
        /// </summary>
        public object Add(T item, IDataProvider provider)
        {
            var query = item.ToInsertQuery(provider).GetCommand();
            object result = null;
            if(query != null)
            {
                if (provider.Client == DataClient.SqlClient)
                {
                    //add in SCOPE_INDENTITY so we can pull back the ID
                    query.CommandSql += "; SELECT SCOPE_IDENTITY() as new_id";
                }

                var rdr = provider.ExecuteReader(query);
                if (rdr.Read())
                    result = rdr[0];
                // repopulate primary key column with newly generated ID
                if (result != null && result != DBNull.Value) {

                    try {
                        var tbl = provider.FindOrCreateTable(typeof(T));
                        var prop = item.GetType().GetProperty(tbl.PrimaryKey.Name);
                        var settable = result.ChangeTypeTo(prop.PropertyType);
                        prop.SetValue(item, settable, null);

                    } catch (Exception x) {
                        //swallow it - I don't like this per se but this is a convenience and we
                        //don't want to throw the whole thing just because we can't auto-set the value
                    }
                }

            }
            return result;
        }

        /// <summary>
        /// Adds a bunch of T items 
        ///</summary>
        public void Add(IEnumerable<T> items) {
            Add(items, _db.Provider);

        }

      
        /// <summary>
        /// Adds a bunch of T items 
        ///</summary>
        public void Add(IEnumerable<T> items, IDataProvider provider)
        {
            RunQueryWithToQueryFunc(new ToSqlQuery(ToInsertQuery), items, provider);
        }

        /// <summary>
        /// Updates the passed-in T 
        /// </summary>
        public int Update(T item)
        {
            return Update(item, _db.Provider);
        }

        /// <summary>
        /// Updates the passed-in T 
        /// </summary>
        public int Update(T item, IDataProvider provider)
        {
            int result = 0;
            var query = item.ToUpdateQuery(provider).GetCommand();
            if (query != null)
                result = provider.ExecuteQuery(query);
            return result;
        }


        /// <summary>
        /// Updates the passed-in T 
        /// </summary>
        public int Update(IEnumerable<T> items){
            return Update(items, _db.Provider);
        }

        /// <summary>
        /// Updates the passed-in T 
        /// </summary>
        public int Update(IEnumerable<T> items, IDataProvider provider)
        {
            return RunQueryWithToQueryFunc(new ToSqlQuery(ToUpdateQuery), items, provider);
        }

                /// <summary>
        /// Deletes the passed-in T items 
        /// </summary>
        public int Delete(IEnumerable<T> items) {

            return Delete(items, _db.Provider);
        }


        /// <summary>
        /// Deletes the passed-in T items 
        /// </summary>
        public int Delete(IEnumerable<T> items, IDataProvider provider)
        {
            return RunQueryWithToQueryFunc(new ToSqlQuery(ToDeleteQuery), items, provider);
        }


        

        /// <summary>
        /// Deletes the passed-in T item 
        /// </summary>
        public int Delete(T item) {
            return Delete(item, _db.Provider);
        }


        /// <summary>
        /// Deletes the passed-in T item 
        /// </summary>
        public int Delete(T item, IDataProvider provider)
        {
            int result = 0;
            var query = item.ToDeleteQuery(provider).GetCommand();
            if (query != null)
                result = provider.ExecuteQuery(query);
            return result;
        }

                /// <summary>
        /// Deletes the T item  by Primary Key
        /// </summary>
        public int Delete(object key) {
            return Delete(key, _db.Provider);

        }


        /// <summary>
        /// Deletes the T item  by Primary Key
        /// </summary>
        public int Delete(object key, IDataProvider provider)
        {
            ITable tbl = _db.FindTable(typeof(T).Name);
            int result = 0;
            if(tbl != null)
                result = new Delete<T>(provider).Where(tbl.PrimaryKey.Name).IsEqualTo(key).Execute();
            return result;
        }

        /// <summary>
        /// Deletes 0 to n T items from the Database based on the passed-in Expression
        /// </summary>
        public int DeleteMany(Expression<Func<T, bool>> expression) {
            return DeleteMany(expression, _db.Provider);
        }

        /// <summary>
        /// Deletes 0 to n T items from the Database based on the passed-in Expression
        /// </summary>
        public int DeleteMany(Expression<Func<T, bool>> expression, IDataProvider provider)
        {
            var cmd= _db.Delete(expression).GetCommand();
            return provider.ExecuteQuery(cmd);
        }
        #endregion

        #region delegate methods
        private delegate ISqlQuery ToSqlQuery(T item, IDataProvider provider);

        private ISqlQuery ToInsertQuery(T item, IDataProvider provider)
        {
            return item.ToInsertQuery(provider);
        }

        private ISqlQuery ToDeleteQuery(T item, IDataProvider provider)
        {
            return item.ToDeleteQuery(provider);
        }

        private ISqlQuery ToUpdateQuery(T item, IDataProvider provider)
        {
            return item.ToUpdateQuery(provider);
        }
        #endregion

        /// <summary>
        /// Run query with provided function
        /// </summary>
        /// <param name="func">A function to get query</param>
        /// <param name="items">The items </param>
        /// <param name="provider">The provider</param>
        /// <returns></returns>
        private int RunQueryWithToQueryFunc(ToSqlQuery func, IEnumerable<T> items, IDataProvider provider)
        {
            BatchQuery bQuery = new BatchQuery(provider);
            int result = 0;

            foreach (T item in items)
            {
                var query = func(item, provider);
                bQuery.Queue(query);
            }
            result = bQuery.Execute();
            return result;
        }
    }
}