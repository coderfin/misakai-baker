﻿// ccset.cs
//
// Copyright 2010 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using System.Text;

namespace Baker.Text
{
    internal class JsConditionalCompilationSet : JsConditionalCompilationStatement
    {
        private JsAstNode m_value;

        public JsAstNode Value
        {
            get { return m_value; }
            set
            {
                m_value.IfNotNull(n => n.Parent = (n.Parent == this) ? null : n.Parent);
                m_value = value;
                m_value.IfNotNull(n => n.Parent = this);
            }
        }

        public string VariableName { get; set; }

        public JsConditionalCompilationSet(JsContext context, JsParser parser)
            : base(context, parser)
        {
        }

        public override IEnumerable<JsAstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Value);
            }
        }

        public override void Accept(IJsVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override bool ReplaceChild(JsAstNode oldNode, JsAstNode newNode)
        {
            if (Value == oldNode)
            {
                Value = newNode;
                return true;
            }
            return false;
        }
    }
}
