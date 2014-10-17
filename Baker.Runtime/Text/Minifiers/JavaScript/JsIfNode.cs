// if.cs
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

using System;
using System.Collections.Generic;
using System.Text;

namespace Baker.Text
{

    internal sealed class JsIfNode : JsAstNode
    {
        private JsAstNode m_condition;
        private JsBlock m_trueBlock;
        private JsBlock m_falseBlock;

        public JsAstNode Condition
        {
            get { return m_condition; }
            set
            {
                m_condition.IfNotNull(n => n.Parent = (n.Parent == this) ? null : n.Parent);
                m_condition = value;
                m_condition.IfNotNull(n => n.Parent = this);
            }
        }

        public JsBlock TrueBlock
        {
            get { return m_trueBlock; }
            set
            {
                m_trueBlock.IfNotNull(n => n.Parent = (n.Parent == this) ? null : n.Parent);
                m_trueBlock = value;
                m_trueBlock.IfNotNull(n => n.Parent = this);
            }
        }

        public JsBlock FalseBlock
        {
            get { return m_falseBlock; }
            set
            {
                m_falseBlock.IfNotNull(n => n.Parent = (n.Parent == this) ? null : n.Parent);
                m_falseBlock = value;
                m_falseBlock.IfNotNull(n => n.Parent = this);
            }
        }

        public JsContext ElseContext { get; set; }

        public override JsContext TerminatingContext
        {
            get
            {
                // if we have one, return it.
                var term = base.TerminatingContext;
                if (term == null)
                {
                    // we didn't have a terminator. See if there's an else-block. If so,
                    // return it's terminator (if any)
                    if (FalseBlock != null)
                    {
                        term = FalseBlock.TerminatingContext;
                    }
                    else
                    {
                        // no else-block. Return the true-block's, if there is one.
                        term = TrueBlock.IfNotNull(b => b.TerminatingContext);
                    }
                }

                return term;
            }
        }

        public JsIfNode(JsContext context, JsParser parser)
            : base(context, parser)
        {
        }

        public override void Accept(IJsVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public void SwapBranches()
        {
            JsBlock temp = m_trueBlock;
            m_trueBlock = m_falseBlock;
            m_falseBlock = temp;
        }

        public override IEnumerable<JsAstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Condition, TrueBlock, FalseBlock);
            }
        }

        public override bool ReplaceChild(JsAstNode oldNode, JsAstNode newNode)
        {
            if (Condition == oldNode)
            {
                Condition = newNode;
                return true;
            }
            if (TrueBlock == oldNode)
            {
                TrueBlock = ForceToBlock(newNode);
                return true;
            }
            if (FalseBlock == oldNode)
            {
                FalseBlock = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        internal override bool RequiresSeparator
        {
            get
            {
                // if we have an else block, then the if statement
                // requires a separator if the else block does. 
                // otherwise only if the true case requires one.
                if (FalseBlock != null && FalseBlock.Count > 0)
                {
                    return FalseBlock.RequiresSeparator;
                }
                if (TrueBlock != null && TrueBlock.Count > 0)
                {
                    return TrueBlock.RequiresSeparator;
                }
                return false;
            }
        }

        internal override bool EncloseBlock(EncloseBlockType type)
        {
            // if there's an else block, recurse down that branch.
            // if we aren't forcing braces and the block contains nothing, then we don't
            // really have a false block.
            if (FalseBlock != null && (FalseBlock.ForceBraces || FalseBlock.Count > 0))
            {
                return FalseBlock.EncloseBlock(type);
            }
            else if (type == EncloseBlockType.IfWithoutElse)
            {
                // there is no else branch -- we might have to enclose the outer block
                return true;
            }
            else if (TrueBlock != null)
            {
                return TrueBlock.EncloseBlock(type);
            }
            return false;
        }
    }
}