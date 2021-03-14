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
        public int IndentCount;

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

        /// <summary>
        /// 当前处理的行是否在子中括号内
        ///     如果是子中括号的内容交给子Bracket处理
        /// </summary>
        /// <param name="LineEdit">当前编辑的行</param>
        /// <returns></returns>
        private bool EditLineIsInChildren(EditPoint LineEdit)
        {
            if (LineEdit == null) return false;

            foreach (IndentSquareBracket Child in Children)
            {
                //大于子Bracket的开始位置
                bool bGreaterStart = LineEdit.Line > Child.Start.Line;
                //小于子Bracket的结束位置
                bool bLessEnd = LineEdit.Line < Child.End.Line;
                //既大于又小于，说明在子Bracket内，跳过不处理，等待子Bracket处理
                if (bGreaterStart && bLessEnd)
                    return true;
            }

            return false;
        }

        public void Execute()
        {
            foreach (IndentSquareBracket Bracket in Children)
            {
                //向下一行，[ 不需要缩进，后续子Bracket的缩进是建立在 [ 的缩进深度的基础上
                Bracket.Start.LineDown();

                EditPoint CurrentEditLintPoint = Bracket.Start.CreateEditPoint();
                if (CurrentEditLintPoint == null) continue;

                //循环本区间的每行，删除空白，后面根据层级深度，添加缩进
                do
                {
                    //检查是否在在括号内，子括号内的内容，交给子括号处理
                    if (Bracket.EditLineIsInChildren(CurrentEditLintPoint))
                    {
                        CurrentEditLintPoint.LineDown();
                        continue;
                    }

                    //处理缩进，对比应该的缩进深度，大于缩进深度，则剔除，小于则添加
                    HandleIndent(Bracket, CurrentEditLintPoint);

                    CurrentEditLintPoint.LineDown();
                } while (CurrentEditLintPoint.Line <= Bracket.End.Line);//同时处理最后一个 ] 跟 [ 的深度保持一致

                //遍历执行所有的子Bracket
                Bracket.Execute();
            }
        }

        /// <summary>
        /// 处理缩进
        /// </summary>
        /// <param name="Bracket">一对方括号内容</param>
        /// <param name="CurrentEditLintPoint">当前行的编辑点</param>
        private static void HandleIndent(IndentSquareBracket Bracket, EditPoint CurrentEditLintPoint)
        {
            //把处理点移动到一行的开始位置
            CurrentEditLintPoint.StartOfLine();

            //是不是最后的右中括号 ]
            bool IsEndRightBracket = CurrentEditLintPoint.Line == Bracket.End.Line;
            string LineText = CurrentEditLintPoint.GetText(CurrentEditLintPoint.LineLength);

            //整行从开头就被注释，则退出
            if (LineText.StartsWith("//")) return;
            //如果这行是以#开头，则退出，针对#if之类的
            if (LineText.StartsWith("#")) return;

            //剔除这行最前面的制表符
            string CurrentLineText = LineText.TrimStart('\t');

            //未处理该行的缩进个数
            int OldIndentCount = LineText.Length - CurrentLineText.Length;
            //中括号之间的内容，应该缩进的个数
            int ContentIndentCount = Bracket.IndentCount + (IsEndRightBracket ? 0 : 1);
            //待处理缩进个数
            int IndentCountAbs = Math.Abs(OldIndentCount - ContentIndentCount);

            //如果小于应该缩进的个数，则添加
            if (OldIndentCount < ContentIndentCount)
                CurrentEditLintPoint.Indent(null, IndentCountAbs);
            //否则取消相应个数
            else if (OldIndentCount > ContentIndentCount)
                CurrentEditLintPoint.Unindent(null, IndentCountAbs);
        }

        public void FindBracket()
        {
            EditPoint EndPoint = null;
            TextRanges dummy = null;
            EditPoint StartPoint = Start.CreateEditPoint();
            StartPoint.LineDown();
            //FindPattern 不能指定结束位置
            while (StartPoint.FindPattern(MaximumBracketPattern, StandardFindOptions, ref EndPoint, ref dummy))
            {
                //所以，需要 判读匹配的开始位置是否在，被包含的区间外
                if (StartPoint.Line >= End.Line)
                    break;//在外面，则跳出，转入数组的下一个

                if (StartPoint != null && EndPoint != null)
                {
                    IndentSquareBracket Child = new IndentSquareBracket(StartPoint, EndPoint, this);

                    StartPoint.StartOfLine();

                    int LineIndentCount;
                    //当父节区间为空时，取父区间的缩进，否则在父区间的基础上+1，逐级实现缩进
                    if (Parent == null)
                    {
                        string LineText = StartPoint.GetText(StartPoint.LineLength);
                        string textTemp = LineText.TrimStart('\t');
                        LineIndentCount = LineText.Length - textTemp.Length;//获取当前的缩进深度
                    }
                    else
                    {
                        LineIndentCount = IndentCount + 1;
                    }

                    Child.IndentCount = LineIndentCount;
                    Children.Add(Child);

                    //子Bracket递归查找
                    Child.FindBracket();
                }

                //再从区间的结束位置向后在下一个最大的包含区间
                StartPoint = EndPoint;
            }
        }
    }

    /// <summary>
    /// 被方括号包裹的行，缩进；可以处理 Unreal Engine Slate 格式
    ///     根据[]进行层级缩进
    /// </summary>
    internal class BracketIndentLogic
    {
        #region Fields

        private readonly CodeMaidPackage _package;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// The singleton instance of the <see cref="ASlateInsertIndent" /> class.
        /// </summary>
        private static BracketIndentLogic _instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="ASlateInsertIndent" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        private BracketIndentLogic(CodeMaidPackage package)
        {
            _package = package;
        }

        /// <summary>
        /// Gets an instance of the <see cref="ASlateInsertIndent" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <returns>An instance of the <see cref="ASlateInsertIndent" /> class.</returns>
        internal static BracketIndentLogic GetInstance(CodeMaidPackage package)
        {
            return _instance ?? (_instance = new BracketIndentLogic(package));
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
            bracket.FindBracket();
            bracket.Execute();
        }
    }
}