using EnvDTE;
using SteveCadwallader.CodeMaid.Helpers;
using SteveCadwallader.CodeMaid.Properties;
using System;
using System.Collections.Generic;

namespace SteveCadwallader.CodeMaid.Logic.Cleaning
{
    /// <summary>
    /// 缩进方括号
    /// </summary>
    internal class IndentSquareBracket
    {
        internal const int StandardFindOptions = (int)(vsFindOptions.vsFindOptionsRegularExpression | vsFindOptions.vsFindOptionsMatchInHiddenText);

        //一组成对的最大方括号，正则匹配表达式，\s不加会匹配到在同一行的，数组索引或者lambda表达式
        private static readonly string MaximumBracketPattern = @"\[\s[^[\]]*(((?'Open'\[)|(?'-Open'\])|[^[\]]*)+)*(?(Open)(?!))\]";

        //缩进层级
        public int Depth;

        //匹配到的成对最大方括号开始位置
        public EditPoint Start;

        //匹配到的成对最大方括号结束位置
        public EditPoint End;

        //一个搜索区间内包含的最大方括号对
        private List<IndentSquareBracket> Children = new List<IndentSquareBracket>();

        //父级方括号对
        public IndentSquareBracket Parent;

        public IndentSquareBracket(TextPoint start, TextPoint end, IndentSquareBracket parent)
        {
            Start = start.CreateEditPoint();//需要创建一个新的，相当于复制，不然后面操作，会影响到其他的
            End = end.CreateEditPoint();
            Parent = parent;
        }

        public void Execute()
        {
            EditPoint EndPoint = null;
            TextRanges dummy = null;
            EditPoint StartPoint = Start.CreateEditPoint();
            //FindPattern 不能指定结束位置
            while (StartPoint.FindPattern(MaximumBracketPattern, StandardFindOptions, ref EndPoint, ref dummy))
            {
                //所以，需要 判读匹配的开始位置是否在，被包含的区间外
                if (StartPoint.Line >= End.Line)
                    break;//在外面，则跳出，转入数组的下一个

                if (StartPoint != null && EndPoint != null)
                {
                    IndentSquareBracket Child = new IndentSquareBracket(StartPoint, EndPoint, this);
                    Child.Depth = Parent == null ? StartPoint.LineLength : Depth + 1;//当父节区间为空时，取父区间的缩进，否则在父区间的基础上+1，逐级实现缩进
                    Children.Add(Child);
                }
                //再从区间的结束位置向后在下一个最大的包含区间
                StartPoint = EndPoint;
            }

            foreach (IndentSquareBracket item in Children)
            {
                //向下一行，[ 不需要缩进
                item.Start.LineDown();

                //循环本区间的每行，删除空白，后面根据层级深度，添加缩进
                EditPoint TempEdit = item.Start.CreateEditPoint();
                do
                {
                    TempEdit.DeleteWhitespace();
                    TempEdit.LineDown();
                } while (TempEdit.Line < item.End.Line);

                //向上一行，] 不需要缩进
                item.End.LineUp();
                //根据层级深度，添加缩进
                //[
                //  ****
                //  ****
                //]
                item.Start.Indent(item.End, item.Depth);

                //遍历执行数组的每项
                item.Execute();
            }
        }
    }

    /// <summary>
    /// 缩进 Unreal Engine Slate 格式
    ///     根据[]进行层级缩进
    /// </summary>
    internal class UnrealSlateIndent
    {
        #region Fields

        private readonly CodeMaidPackage _package;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// The singleton instance of the <see cref="ASlateInsertIndent" /> class.
        /// </summary>
        private static UnrealSlateIndent _instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="ASlateInsertIndent" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        private UnrealSlateIndent(CodeMaidPackage package)
        {
            _package = package;
        }

        /// <summary>
        /// Gets an instance of the <see cref="ASlateInsertIndent" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>An instance of the <see cref="ASlateInsertIndent" /> class.</returns>
        internal static UnrealSlateIndent GetInstance(CodeMaidPackage package)
        {
            return _instance ?? (_instance = new UnrealSlateIndent(package));
        }

        #endregion Constructors

        /// <summary>
        /// Indent according to bracket hierarchy
        /// </summary>
        /// <param name="textDocument">The text document to cleanup.</param>
        internal void InsertIndent(TextDocument textDocument)
        {
            TextPoint cursor = textDocument.StartPoint.CreateEditPoint();
            IndentSquareBracket bracket = new IndentSquareBracket(cursor.Parent.StartPoint, cursor.Parent.EndPoint, null);
            //bracket.Execute();
        }
    }
}