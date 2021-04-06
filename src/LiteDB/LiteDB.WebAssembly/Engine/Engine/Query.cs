﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using static LiteDB.Constants;

namespace LiteDB.Engine
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Run query over collection using a query definition. 
        /// Returns a new IBsonDataReader that run and return first document result (open transaction)
        /// </summary>
        public async Task<IBsonDataReader> QueryAsync(string collection, Query query)
        {
            if (string.IsNullOrWhiteSpace(collection)) throw new ArgumentNullException(nameof(collection));
            //if (query == null) throw new ArgumentNullException(nameof(query));

            //IEnumerable<BsonDocument> source = null;

            // test if is an system collection
            // if (collection.StartsWith("$"))
            // {
            //     SqlParser.ParseCollection(new Tokenizer(collection), out var name, out var options);
            // 
            //     // get registered system collection to get data source
            //     var sys = this.GetSystemCollection(name);
            // 
            //     source = sys.Input(options);
            //     collection = sys.Name;
            // }

            var exec = new QueryExecutor(this, _header.Pragmas, collection, query, null);

            return await exec.ExecuteQuery();
        }
    }
}