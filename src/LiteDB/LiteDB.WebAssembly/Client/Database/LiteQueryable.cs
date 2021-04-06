﻿using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

using static LiteDB.Constants;

namespace LiteDB
{
    /// <summary>
    /// An IQueryable-like class to write fluent query in documents in collection.
    /// </summary>
    public class LiteQueryable<T> : ILiteQueryable<T>
    {
        protected readonly ILiteEngine _engine;
        protected readonly BsonMapper _mapper;
        protected readonly string _collection;
        protected readonly Query _query;

        // indicate that T type are simple and result are inside first document fields (query always return a BsonDocument)
        private readonly bool _isSimpleType = Reflection.IsSimpleType(typeof(T));

        internal LiteQueryable(ILiteEngine engine, BsonMapper mapper, string collection, Query query)
        {
            _engine = engine;
            _mapper = mapper;
            _collection = collection;
            _query = query;
        }

        #region Includes

        /// <summary>
        /// Load cross reference documents from path expression (DbRef reference)
        /// </summary>
        public ILiteQueryable<T> Include<K>(Expression<Func<T, K>> path)
        {
            _query.Includes.Add(_mapper.GetExpression(path));
            return this;
        }

        /// <summary>
        /// Load cross reference documents from path expression (DbRef reference)
        /// </summary>
        public ILiteQueryable<T> Include(BsonExpression path)
        {
            _query.Includes.Add(path);
            return this;
        }

        /// <summary>
        /// Load cross reference documents from path expression (DbRef reference)
        /// </summary>
        public ILiteQueryable<T> Include(List<BsonExpression> paths)
        {
            _query.Includes.AddRange(paths);
            return this;
        }

        #endregion

        #region Where

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(BsonExpression predicate)
        {
            _query.Where.Add(predicate);
            return this;
        }

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(string predicate, BsonDocument parameters)
        {
            _query.Where.Add(BsonExpression.Create(predicate, parameters));
            return this;
        }

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(string predicate, params BsonValue[] args)
        {
            _query.Where.Add(BsonExpression.Create(predicate, args));
            return this;
        }

        /// <summary>
        /// Filters a sequence of documents based on a predicate expression
        /// </summary>
        public ILiteQueryable<T> Where(Expression<Func<T, bool>> predicate)
        {
            return this.Where(_mapper.GetExpression(predicate));
        }

        #endregion

        #region OrderBy

        /// <summary>
        /// Sort the documents of resultset in ascending (or descending) order according to a key (support only one OrderBy)
        /// </summary>
        public ILiteQueryable<T> OrderBy(BsonExpression keySelector, int order = Query.Ascending)
        {
            if (_query.OrderBy != null) throw new ArgumentException("ORDER BY already defined in this query builder");

            _query.OrderBy = keySelector;
            _query.Order = order;
            return this;
        }

        /// <summary>
        /// Sort the documents of resultset in ascending (or descending) order according to a key (support only one OrderBy)
        /// </summary>
        public ILiteQueryable<T> OrderBy<K>(Expression<Func<T, K>> keySelector, int order = Query.Ascending)
        {
            return this.OrderBy(_mapper.GetExpression(keySelector), order);
        }

        /// <summary>
        /// Sort the documents of resultset in descending order according to a key (support only one OrderBy)
        /// </summary>
        public ILiteQueryable<T> OrderByDescending(BsonExpression keySelector) => this.OrderBy(keySelector, Query.Descending);

        /// <summary>
        /// Sort the documents of resultset in descending order according to a key (support only one OrderBy)
        /// </summary>
        public ILiteQueryable<T> OrderByDescending<K>(Expression<Func<T, K>> keySelector) => this.OrderBy(keySelector, Query.Descending);

        #endregion

        #region GroupBy

        /// <summary>
        /// Groups the documents of resultset according to a specified key selector expression (support only one GroupBy)
        /// </summary>
        public ILiteQueryable<T> GroupBy(BsonExpression keySelector)
        {
            if (_query.GroupBy != null) throw new ArgumentException("GROUP BY already defined in this query");

            _query.GroupBy = keySelector;
            return this;
        }

        #endregion

        #region Having

        /// <summary>
        /// Filter documents after group by pipe according to predicate expression (requires GroupBy and support only one Having)
        /// </summary>
        public ILiteQueryable<T> Having(BsonExpression predicate)
        {
            if (_query.Having != null) throw new ArgumentException("HAVING already defined in this query");

            _query.Having = predicate;
            return this;
        }

        #endregion

        #region Select

        /// <summary>
        /// Transform input document into a new output document. Can be used with each document, group by or all source
        /// </summary>
        public ILiteQueryableResult<BsonDocument> Select(BsonExpression selector)
        {
            _query.Select = selector;

            return new LiteQueryable<BsonDocument>(_engine, _mapper, _collection, _query);
        }

        /// <summary>
        /// Project each document of resultset into a new document/value based on selector expression
        /// </summary>
        public ILiteQueryableResult<K> Select<K>(Expression<Func<T, K>> selector)
        {
            if (_query.GroupBy != null) throw new ArgumentException("Use Select(BsonExpression selector) when using GroupBy query");

            _query.Select = _mapper.GetExpression(selector);

            return new LiteQueryable<K>(_engine, _mapper, _collection, _query);
        }

        #endregion

        #region Offset/Limit/ForUpdate

        /// <summary>
        /// Execute query locking collection in write mode. This is avoid any other thread change results after read document and before transaction ends
        /// </summary>
        public ILiteQueryableResult<T> ForUpdate()
        {
            _query.ForUpdate = true;
            return this;
        }

        /// <summary>
        /// Bypasses a specified number of documents in resultset and retun the remaining documents (same as Skip)
        /// </summary>
        public ILiteQueryableResult<T> Offset(int offset)
        {
            _query.Offset = offset;
            return this;
        }

        /// <summary>
        /// Bypasses a specified number of documents in resultset and retun the remaining documents (same as Offset)
        /// </summary>
        public ILiteQueryableResult<T> Skip(int offset) => this.Offset(offset);

        /// <summary>
        /// Return a specified number of contiguous documents from start of resultset
        /// </summary>
        public ILiteQueryableResult<T> Limit(int limit)
        {
            _query.Limit = limit;
            return this;
        }

        #endregion

        #region Execute Result

        /// <summary>
        /// Execute query and returns resultset as generic BsonDataReader
        /// </summary>
        public async Task<IBsonDataReader> ExecuteReaderAsync()
        {
            _query.ExplainPlan = false;

            return await _engine.QueryAsync(_collection, _query);
        }

        /// <summary>
        /// Execute query and return resultset as IEnumerable of documents
        /// </summary>
        public async IAsyncEnumerable<BsonDocument> ToDocumentsAsync()
        {
            await using (var reader = await this.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    yield return reader.Current as BsonDocument;
                }
            }
        }

        /// <summary>
        /// Execute query and return resultset as IEnumerable of T. If T is a ValueType or String, return values only (not documents)
        /// </summary>
        public async IAsyncEnumerable<T> ToAsyncEnumerable()
        {
            if (_isSimpleType)
            {
                await foreach(var doc in this.ToDocumentsAsync())
                {
                    var key = doc.Keys.First();

                    yield return (T)_mapper.Deserialize(typeof(T), key);
                }
            }
            else
            {
                await foreach (var doc in this.ToDocumentsAsync())
                {
                    var key = doc.Keys.First();

                    yield return (T)_mapper.Deserialize(typeof(T), doc);
                }
            }
        }

        /// <summary>
        /// Execute query and return results as a List
        /// </summary>
        public Task<List<T>> ToListAsync() => this.ToAsyncEnumerable().ToListAsync();

        /// <summary>
        /// Execute query and return results as an Array
        /// </summary>
        public Task<T[]> ToArrayAsync() => this.ToAsyncEnumerable().ToArrayAsync();

        /// <summary>
        /// Get execution plan over current query definition to see how engine will execute query
        /// </summary>
        public async Task<BsonDocument> GetPlanAsync()
        {
            _query.ExplainPlan = true;

            await using var reader = await _engine.QueryAsync(_collection, _query);

            return reader.Current.AsDocument;
        }

        #endregion

        #region Execute Single/First

        /// <summary>
        /// Returns the only document of resultset, and throw an exception if there not exactly one document in the sequence
        /// </summary>
        public Task<T> SingleAsync() => this.ToAsyncEnumerable().SingleAsync();

        /// <summary>
        /// Returns the only document of resultset, or null if resultset are empty; this method throw an exception if there not exactly one document in the sequence
        /// </summary>
        public Task<T> SingleOrDefaultAsync() => this.ToAsyncEnumerable().SingleOrDefaultAsync();

        /// <summary>
        /// Returns first document of resultset
        /// </summary>
        public Task<T> FirstAsync() => this.ToAsyncEnumerable().FirstAsync();

        /// <summary>
        /// Returns first document of resultset or null if resultset are empty
        /// </summary>
        public Task<T> FirstOrDefaultAsync() => this.ToAsyncEnumerable().FirstOrDefaultAsync();

        public Task<int> CountAsync() => this.ToAsyncEnumerable().CountAsync();

        public Task<long> LongCountAsync() => this.ToAsyncEnumerable().LongCountAsync();

        public Task<bool> ExistsAsync() => this.ToAsyncEnumerable().AnyAsync();

        #endregion
    }
}