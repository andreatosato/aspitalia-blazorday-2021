﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB.Engine;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    internal partial class SqlParser
    {
        /// <summary>
        /// DELETE {collection} WHERE {whereExpr}
        /// </summary>
        private async Task<BsonDataReader> ParseDelete()
        {
            _tokenizer.ReadToken().Expect("DELETE");

            var collection = _tokenizer.ReadToken().Expect(TokenType.Word).Value;

            BsonExpression where = null;

            if (_tokenizer.LookAhead().Is("WHERE"))
            {
                // read WHERE
                _tokenizer.ReadToken();

                where = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters);
            }

            _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

            _tokenizer.ReadToken();

            var result = await _engine.DeleteManyAsync(collection, where);

            return new BsonDataReader(result);
        }
    }
}