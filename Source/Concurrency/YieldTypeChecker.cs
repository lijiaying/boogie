﻿#if QED


using System;
using System.Collections;
using System.Collections.Generic;

using System.Linq;
using System.Text;
using Microsoft.Boogie;
//! using Microsoft.Automata;

using System.Diagnostics.Contracts;
using Microsoft.Boogie.AbstractInterpretation;
using Microsoft.Boogie.GraphUtil;
namespace Microsoft.Boogie
{
    /*
     Summary:
     * 
     */
    class YieldTypeChecker
    {/*
          CharSetSolver yieldTypeCheckerAutomatonSolver;
          string yieldTypeCheckerRegex = @"^((1|2)+(3|4))*((D)+(((5|6))+((7|8))+((1|2))+((3|4)))*[A]((9)+(7)+(3))*)*$";
          Char[][] yieldTypeCheckerVocabulary;
          BvSet yielTypeCheckerBvSet;
          Automaton<BvSet> yieldTypeCheckerAutomaton;
          */

        private YieldTypeChecker()
        {
            /*
            yieldTypeCheckerAutomatonSolver = new CharSetSolver(BitWidth.BV7);
            yieldTypeCheckerVocabulary = new char[][] {
                new char[]{'1','2'},
                new char[]{'3','4'},
                new char[]{'5','6'},
                new char[]{'7','8'},
                new char[]{'9','A'},
                new char[]{'B','C'},
                new char[]{'D','E'}
            };
            yielTypeCheckerBvSet = yieldTypeCheckerAutomatonSolver.MkRangesConstraint(false, yieldTypeCheckerVocabulary);
            yieldTypeCheckerAutomaton = yieldTypeCheckerAutomatonSolver.Convert(yieldTypeCheckerRegex); //accepts strings that match the regex r

            */
        }

        public static void AddChecker(LinearTypeChecker linearTypeChecker)
        {
            Program yielTypeCheckedProgram = linearTypeChecker.program;
            YieldTypeChecker regExprToAuto = new YieldTypeChecker();
            foreach (var decl in yielTypeCheckedProgram.TopLevelDeclarations)
            {
                Implementation impl = decl as Implementation;
                if (impl != null)
                {
                    if (impl.Blocks.Count > 0)
                    {
                        int phaseNumSpecImpl = 0;
                        foreach (Ensures e in impl.Proc.Ensures)
                        {
                            phaseNumSpecImpl = QKeyValue.FindIntAttribute(e.Attributes, "phase", int.MaxValue);

                        }
                        YieldTypeCheckerCore.TypeCheck(impl, phaseNumSpecImpl, 5);


                    }
                }
            }
        }
    }


    class YieldTypeCheckerCore : StandardVisitor
    {

        CheckingContext checkingContext;
        int errorCount;


        Implementation ytypeChecked;
        // int numCallInEncImpl; // number of callcmds in impl
        int specPhaseNumImpl; // impl.proc's spec num
        int yTypeCheckCurrentPhaseNum;// current phase of yield type checking
        int programCounter; // initial state of the impl

        //! Take of that : Dictionary<Tuple<int, int>, HashSet<int>> backwardEdgesPerYield; //backward edges per yield 

        HashSet<CallCmd> callCmdsInImpl; //  callCmdsInImpl[Implementation] ==> Set = { call1, call2, call3 ... }
        int numCallCmdInEnclosingProc; // Number of CallCmds in       

        Dictionary<Tuple<int, int>, string> verticesToEdges; // Tuple<int,int> --> "Y" : State(k) --Y--> State(k+1)
        HashSet<Tuple<int, int>> yieldTypeCheckGraph; // (-inf ph0 ] (ph0 ph1] (ph1 ph2] ..... (phk phk+1] where phk+1 == atcSpecPhsNumTypeCheckedProc
        // Dictionary<Implementation, Automaton<YieldTypeSet>> yieldTypeCheckAutomaton; // To be generated with input yieldTypeCheckGraph

        List<Tuple<int, int>> phaseIntervals; // (-inf ph0 ] (ph0 ph1] (ph1 ph2] ..... (phk phk+1] intervals


        private YieldTypeCheckerCore(Implementation toTypeCheck, int specPhaseNum, int currentPhsCheckNum)
        {
            ytypeChecked = toTypeCheck;
            this.errorCount = 0;
            this.checkingContext = new CheckingContext(null);
            // numCallInEncImpl = 0;
            specPhaseNumImpl = specPhaseNum;
            yTypeCheckCurrentPhaseNum = currentPhsCheckNum;
            programCounter = Math.Abs(Guid.NewGuid().GetHashCode());


            /*Utility Maps*/
            phaseIntervals = new List<Tuple<int, int>>();
            callCmdsInImpl = new HashSet<CallCmd>();
            numCallCmdInEnclosingProc = 0;

            // Graph and Automaton
            yieldTypeCheckGraph = new HashSet<Tuple<int, int>>();
            // yieldTypeCheckAutomaton = new Dictionary<Implementation, Automaton<YieldTypeSet>>();
            verticesToEdges = new Dictionary<Tuple<int, int>, string>();
            /*Console.Write("\n Proc being checked is " + ytypeChecked.Proc.Name+ "\n"); 
            Console.Write("\n Specnum is " + specPhaseNumImpl.ToString() + "\n");
            Console.Write("\n PhaseCheckNum is " + yTypeCheckCurrentPhaseNum.ToString() + "\n");
            */
        }



        private void CalculateCallCmds(Implementation impl)
        {
            // HashSet<CallCmd> callCmdInBodyEncImpl = new HashSet<CallCmd>();
            List<CallCmd> callCmdInBodyEncImplList = new List<CallCmd>();


            foreach (Block b in impl.Blocks)
            {
                List<Cmd> cmds = new List<Cmd>();
                for (int i = 0; i < b.Cmds.Count; i++)
                {
                    Cmd cmd = b.Cmds[i];
                    if (cmd is CallCmd)
                    {
                        CallCmd callCmd = b.Cmds[i] as CallCmd;
                        numCallCmdInEnclosingProc = numCallCmdInEnclosingProc + 1;



                        if (!(callCmdsInImpl.Contains(callCmd)))
                        {
                            callCmdsInImpl.Add(callCmd);
                            callCmdInBodyEncImplList.Add(callCmd);// 

                        }

                    }
                }

            }

            //Sort all PhaseNum of callCmds inside Implementation 
            List<int> callCmdPhaseNumIndexList = new List<int>();
            for (int i = 0; i < callCmdInBodyEncImplList.Count; i++)
            {
                int tmpPhaseNum = QKeyValue.FindIntAttribute(callCmdInBodyEncImplList[i].Proc.Attributes, "phase", 0);
                callCmdPhaseNumIndexList.Add(tmpPhaseNum);
            }

            callCmdPhaseNumIndexList.Sort();

            //Create Phase Intervals
            for (int i = 0; i < callCmdPhaseNumIndexList.Count; i++)
            {
                //set the initial 
                if (i == 0)
                {
                    Tuple<int, int> initTuple = new Tuple<int, int>(int.MinValue, callCmdPhaseNumIndexList[i]);
                    phaseIntervals.Add(initTuple);
                }
                else
                {
                    Tuple<int, int> intervalToInsert = new Tuple<int, int>(callCmdPhaseNumIndexList[i - 1] + 1, callCmdPhaseNumIndexList[i]);
                    phaseIntervals.Add(intervalToInsert);
                }
            }

        }

        public static bool TypeCheck(Implementation impl, int specPhsNum, int checkPhsNum)
        {


            YieldTypeCheckerCore yieldTypeCheckerPerImpl = new YieldTypeCheckerCore(impl, specPhsNum, checkPhsNum);
            yieldTypeCheckerPerImpl.CalculateCallCmds(impl);
            yieldTypeCheckerPerImpl.BuildAutomatonGraph();
            Console.Write(yieldTypeCheckerPerImpl.PrintGraph(yieldTypeCheckerPerImpl.GetTuplesForGraph()));
            //  yieldTypeCheckerPerImpl.BackwardEdgesOfYields(yieldTypeCheckerPerImpl.GetTuplesForGraph());
            return true;
        }


        public void BuildAutomatonGraph()
        {
            foreach (Block block in ytypeChecked.Blocks)
            {

                for (int i = 0; i < block.Cmds.Count; i++)
                {
                    AddNodeToGraph(block.Cmds[i]);
                }

                if (block.TransferCmd is GotoCmd)
                {
                    GotoCmd gt = block.TransferCmd as GotoCmd;
                    AddNodeToGraphForGotoCmd(gt);
                }
                else if (block.TransferCmd is ReturnCmd)
                {
                    ReturnCmd rt = block.TransferCmd as ReturnCmd;
                    AddNodeToGraphForReturnCmd(rt);
                }
            }


        }

        public Graph<int> GetTuplesForGraph()
        {
            Graph<int> graphFromTuples = new Graph<int>(yieldTypeCheckGraph);
            return graphFromTuples;

        }

        public void AddNodeToGraph(Cmd cmd)
        {
            if (cmd is AssignCmd)
            {
                AssignCmd assignCmd = cmd as AssignCmd;
                AddNodeToGraphForAssignCmd(assignCmd);
            }
            else if (cmd is HavocCmd)
            {
                HavocCmd havocCmd = cmd as HavocCmd;
                AddNodeToGraphForHavocCmd(havocCmd);
            }
            else if (cmd is AssumeCmd)
            {
                AssumeCmd assumeCmd = cmd as AssumeCmd;
                AddNodeToGraphForAssumeCmd(assumeCmd);
            }
            else if (cmd is AssertCmd)
            {
                AssertCmd assertCmd = cmd as AssertCmd;
                AddNodeToGraphForAssertCmd(assertCmd);
            }
            else if (cmd is YieldCmd)
            {
                YieldCmd yieldCmd = cmd as YieldCmd;
                AddNodeToGraphForYieldCmd(yieldCmd);

            }
            else if (cmd is CallCmd)
            {

                CallCmd callCmd = cmd as CallCmd;
                //  Console.Write(" Impelemtation is " + ytypeChecked.Proc.Name+ "\n");
                // Console.Write("CallCmd PhaseCheck Num " + yTypeCheckCurrentPhaseNum.ToString() + "\n");
                //Console.Write("CallCmd PhaseCheck Num "+specPhaseNumImpl.ToString() + "\n");
                AddNodeToGraphForCallCmd(callCmd, yTypeCheckCurrentPhaseNum);

            }
        }

        public void AddNodeToGraphForGotoCmd(GotoCmd cmd)
        {
            int source = programCounter;
            int dest = programCounter;//Math.Abs(Guid.NewGuid().GetHashCode());
            //Console.Write("\n Program counter before goto " + source.ToString() +" \n");
            programCounter = dest;

            // Create a Epsilon transition between them
            Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
            yieldTypeCheckGraph.Add(srcdst);
            verticesToEdges[srcdst] = "N";
        }

        public void AddNodeToGraphForReturnCmd(ReturnCmd cmd)
        {

            // Do nothing !

        }

        public void AddNodeToGraphForYieldCmd(YieldCmd cmd)
        {

            int source = programCounter;
            int dest = Math.Abs(Guid.NewGuid().GetHashCode());
            programCounter = dest;

            Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
            yieldTypeCheckGraph.Add(srcdst);
            verticesToEdges[srcdst] = "Y";

        }

        public void AddNodeToGraphForAssignCmd(AssignCmd cmd)
        {

            int source = programCounter;
            int dest = Math.Abs(Guid.NewGuid().GetHashCode());
            programCounter = dest;

            Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
            yieldTypeCheckGraph.Add(srcdst);
            verticesToEdges[srcdst] = "Q";
        }

        public void AddNodeToGraphForHavocCmd(HavocCmd cmd)
        {

            int source = programCounter;
            int dest = Math.Abs(Guid.NewGuid().GetHashCode());
            programCounter = dest;

            Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
            yieldTypeCheckGraph.Add(srcdst);
            verticesToEdges[srcdst] = "Q";

        }

        public void AddNodeToGraphForAssumeCmd(AssumeCmd cmd)
        {

            int source = programCounter;
            int dest = Math.Abs(Guid.NewGuid().GetHashCode());
            programCounter = dest;

            Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
            yieldTypeCheckGraph.Add(srcdst);
            verticesToEdges[srcdst] = "P";

        }

        public void AddNodeToGraphForAssertCmd(AssertCmd cmd)
        {
            int source = programCounter;
            int dest = Math.Abs(Guid.NewGuid().GetHashCode());
            // Create a Epsilon transition between them
            programCounter = dest;

            Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
            yieldTypeCheckGraph.Add(srcdst);
            verticesToEdges[srcdst] = "N";

        }


        public void AddNodeToGraphForCallCmd(CallCmd cmd, int currentCheckPhsNum)
        {
            int callePhaseNum = 0;
            //Console.Write("\n Add CallCmd for " + cmd.Proc.Name +" is encountered ");

            foreach (Ensures e in cmd.Proc.Ensures)
            {
                callePhaseNum = QKeyValue.FindIntAttribute(e.Attributes, "phase", int.MaxValue);
            }

            if (callePhaseNum > currentCheckPhsNum)
            {
                int source = Math.Abs(Guid.NewGuid().GetHashCode());
                programCounter = source;
                //  Console.Write(" New component with source state " + programCounter.ToString() + " generated");
            }
            else
            {
                /*
                Console.Write("\n Callee " + cmd.Proc.Name + " phase num is " + callePhaseNum.ToString());
                Console.Write("\n Current phase check num is " + currentCheckPhsNum.ToString());
                Console.Write("\n Program Counter is " + programCounter.ToString());
            */

                foreach (Ensures e in cmd.Proc.Ensures)
                {
                    if (QKeyValue.FindBoolAttribute(e.Attributes, "atomic"))
                    {
                        int source = programCounter;
                        int dest = Math.Abs(Guid.NewGuid().GetHashCode());
                        programCounter = dest;
                        Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
                        yieldTypeCheckGraph.Add(srcdst);
                        verticesToEdges[srcdst] = "A";

                    }
                    else if (QKeyValue.FindBoolAttribute(e.Attributes, "right"))
                    {
                        int source = programCounter;
                        int dest = Math.Abs(Guid.NewGuid().GetHashCode());
                        programCounter = dest;
                        Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
                        yieldTypeCheckGraph.Add(srcdst);
                        verticesToEdges[srcdst] = "R";
                    }
                    else if (QKeyValue.FindBoolAttribute(e.Attributes, "left"))
                    {
                        int source = programCounter;
                        int dest = Math.Abs(Guid.NewGuid().GetHashCode());
                        programCounter = dest;
                        Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
                        yieldTypeCheckGraph.Add(srcdst);
                        verticesToEdges[srcdst] = "L";
                    }
                    else if (QKeyValue.FindBoolAttribute(e.Attributes, "both"))
                    {

                        int source = programCounter;
                        int dest = Math.Abs(Guid.NewGuid().GetHashCode());
                        programCounter = dest;
                        Tuple<int, int> srcdst = new Tuple<int, int>(source, dest);
                        yieldTypeCheckGraph.Add(srcdst);
                        verticesToEdges[srcdst] = "B";
                    }

                }

            }

        }


        public Graph<int> CreateGrapFromHashSet(HashSet<Tuple<int, int>> grph)
        {

            Graph<int> graphForAutomaton = new Graph<int>(grph);

            // Do Some transformation on graph here // 

            return graphForAutomaton;

        }
        public string PrintGraph(Graph<int> graph)
        {
            var s = new StringBuilder();
            s.AppendLine("\nImplementation " + ytypeChecked.Proc.Name + " digraph G {");
            foreach (var e in graph.Edges)
                s.AppendLine("  \"" + e.Item1.ToString() + "\" -- " + verticesToEdges[e] + " --> " + "  \"" + e.Item2.ToString() + "\";");
            s.AppendLine("}");

            return s.ToString();

        }
        /*
        public void BackwardEdgesOfYields(Graph<int> graph)
        {            
            foreach (var e in graph.Edges)
            {
                var s = new StringBuilder();

                if (verticesToEdges[e] == "Y")
                {
                    graph.Successors(e.Item1);
                }
            }
        }*/




        private void Error(Absy node, string message)
        {
            checkingContext.Error(node, message);
            errorCount++;
        }



        /*
        internal class YieldTypeSet
        {
            uint elems;

            static internal YieldTypeSet Empty = new YieldTypeSet(0);
            static internal YieldTypeSet Full = new YieldTypeSet(uint.MaxValue);

            internal YieldTypeSet(uint elems)
            {
                this.elems = elems;
            }

            public override bool Equals(object obj)
            {
                YieldTypeSet s = obj as YieldTypeSet;
                if (s == null)
                    return false;
                return elems == s.elems;
            }

            public override int GetHashCode()
            {
                return (int)elems;
            }

            public YieldTypeSet Union(YieldTypeSet s)
            {
                return new YieldTypeSet(elems | s.elems);
            }

            public YieldTypeSet Intersect(YieldTypeSet s)
            {
                return new YieldTypeSet(elems & s.elems);
            }

            public YieldTypeSet Complement()
            {
                return new YieldTypeSet(~elems);
            }

            public override string ToString()
            {
                return string.Format("YieldTypeSet(0x{0})", elems.ToString("X"));
            }
        }


        internal class YieldTypeSetSolver : IBoolAlgMinterm<YieldTypeSet>
        {

            MintermGenerator<YieldTypeSet> mtg; //use the default built-in minterm generator
            internal YieldTypeSetSolver()
            {
                //create the minterm generator for this solver
                mtg = new MintermGenerator<YieldTypeSet>(this);
            }

            public bool AreEquivalent(YieldTypeSet predicate1, YieldTypeSet predicate2)
            {
                return predicate1.Equals(predicate2);
            }

            public YieldTypeSet False
            {
                get { return YieldTypeSet.Empty; }
            }

            public YieldTypeSet MkAnd(IEnumerable<YieldTypeSet> predicates)
            {
                YieldTypeSet res = YieldTypeSet.Full;
                foreach (var s in predicates)
                    res = res.Intersect(s);
                return res;
            }

            public YieldTypeSet MkNot(YieldTypeSet predicate)
            {
                return predicate.Complement();
            }

            public YieldTypeSet MkOr(IEnumerable<YieldTypeSet> predicates)
            {
                YieldTypeSet res = YieldTypeSet.Empty;
                foreach (var s in predicates)
                    res = res.Union(s);
                return res;
            }

            public YieldTypeSet True
            {
                get { return YieldTypeSet.Full; }
            }

            public bool IsSatisfiable(YieldTypeSet predicate)
            {
                return !predicate.Equals(YieldTypeSet.Empty);
            }

            public YieldTypeSet MkAnd(YieldTypeSet predicate1, YieldTypeSet predicate2)
            {
                return predicate1.Intersect(predicate2);
            }

            public YieldTypeSet MkOr(YieldTypeSet predicate1, YieldTypeSet predicate2)
            {
                return predicate1.Union(predicate2);
            }

            public IEnumerable<Pair<bool[], YieldTypeSet>> GenerateMinterms(params YieldTypeSet[] constraints)
            {
                return mtg.GenerateMinterms(constraints);
            }
            public YieldTypeSet Simplify(YieldTypeSet s)
            {
                return s;

            }
        }*/




    }
}


#endif