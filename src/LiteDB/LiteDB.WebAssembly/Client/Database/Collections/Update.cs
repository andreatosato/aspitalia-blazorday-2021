﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using static LiteDB.Constants;

namespace LiteDB
{
    public partial class LiteCollection<T>
    {
        /// <summary>
        /// Update a document in this collection. Returns false if not found document in collection
        /// </summary>
        public async Task<bool> UpdateAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            // get BsonDocument from object
            var doc = _mapper.ToDocument(entity);

            return (await _engine.UpdateAsync(_collection, new BsonDocument[] { doc })) > 0;
        }

        /// <summary>
        /// Update a document in this collection. Returns false if not found document in collection
        /// </summary>
        public async Task<bool> UpdateAsync(BsonValue id, T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

            // get BsonDocument from object
            var doc = _mapper.ToDocument(entity);

            // set document _id using id parameter
            doc["_id"] = id;

            return (await _engine.UpdateAsync(_collection, new BsonDocument[] { doc })) > 0;
        }

        /// <summary>
        /// Update all documents
        /// </summary>
        public Task<int> UpdateAsync(IEnumerable<T> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            return _engine.UpdateAsync(_collection, entities.Select(x => _mapper.ToDocument(x)));
        }

        /// <summary>
        /// Update many documents based on transform expression. This expression must return a new document that will be replaced over current document (according with predicate).
        /// Eg: col.UpdateMany("{ Name: UPPER($.Name), Age }", "_id > 0")
        /// </summary>
        public Task<int> UpdateManyAsync(BsonExpression transform, BsonExpression predicate)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            if (transform.Type != BsonExpressionType.Document)
            {
                throw new ArgumentException("Extend expression must return a document. Eg: `col.UpdateMany('{ Name: UPPER(Name) }', 'Age > 10')`");
            }

            return _engine.UpdateManyAsync(_collection, transform, predicate);
        }

        /// <summary>
        /// Update many document based on merge current document with extend expression. Use your class with initializers. 
        /// Eg: col.UpdateMany(x => new Customer { Name = x.Name.ToUpper(), Salary: 100 }, x => x.Name == "John")
        /// </summary>
        public Task<int> UpdateManyAsync(Expression<Func<T, T>> extend, Expression<Func<T, bool>> predicate)
        {
            if (extend == null) throw new ArgumentNullException(nameof(extend));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var ext = _mapper.GetExpression(extend);
            var pred = _mapper.GetExpression(predicate);

            if (ext.Type != BsonExpressionType.Document)
            {
                throw new ArgumentException("Extend expression must return an anonymous class to be merge with entities. Eg: `col.UpdateMany(x => new { Name = x.Name.ToUpper() }, x => x.Age > 10)`");
            }

            return _engine.UpdateManyAsync(_collection, ext, pred);
        }
    }
}