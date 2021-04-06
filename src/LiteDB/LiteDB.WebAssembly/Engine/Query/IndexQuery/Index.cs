﻿using System;
using System.Collections.Generic;
using System.Linq;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Class that implement higher level of index search operations (equals, greater, less, ...)
    /// </summary>
    internal abstract class Index
    {
        /// <summary>
        /// Index name
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Get/Set index order
        /// </summary>
        public int Order { get; set; }

        internal Index(string name, int order)
        {
            this.Name = name;
            this.Order = order;
        }

        #region Executing Index Search

        /// <summary>
        /// Calculate cost based on type/value/collection - Lower is best (1)
        /// </summary>
        public abstract uint GetCost(CollectionIndex index);

        /// <summary>
        /// Abstract method that must be implement for index seek/scan - Returns IndexNodes that match with index
        /// </summary>
        public abstract IAsyncEnumerable<IndexNode> Execute(IndexService indexer, CollectionIndex index);

        /// <summary>
        /// Find witch index will be used and run Execute method
        /// </summary>
        public virtual async IAsyncEnumerable<IndexNode> Run(CollectionPage col, IndexService indexer)
        {
            // get index for this query
            var index = col.GetCollectionIndex(this.Name);

            if (index == null) throw LiteException.IndexNotFound(this.Name);

            var distinct = new HashSet<PageAddress>();

            await foreach(var node in this.Execute(indexer, index))
            {
                // distinct by dataBlock
                if (distinct.Contains(node.DataBlock) == false)
                {
                    distinct.Add(node.DataBlock);

                    yield return node;
                }
            }
        }

        #endregion
    }
}