using System;
using System.Collections.Generic;

namespace com.radiant.engine.bundle;

/// <summary>
/// Procedural Pac-Man maze generator based on the Tetris algorithm
/// from shaunlebron/pacman-mazegen. Generates symmetric 28×31 mazes.
/// </summary>
public static class PacmanMazeGenerator
{
    private const int CellRows = 9;
    private const int CellCols = 5;
    private const int SubRows = 31;  // CellRows*3+1+3
    private const int SubCols = 16;  // CellCols*3-1+2
    private const int MidCols = 14;  // SubCols-2
    private const int FullCols = 28; // MidCols*2

    private const int Up = 0;
    private const int Rt = 1;
    private const int Dn = 2;
    private const int Lt = 3;

    public static string[] Generate(int Seed = -1)
    {
        var Rng = Seed >= 0 ? new Random(Seed) : new Random();
        for (int Attempt = 0; Attempt < 300; Attempt++)
        {
            var B = new MazeBuilder(Rng);
            var Result = B.Build();
            if (Result != null) return Result;
        }
        return FallbackLayout();
    }

    private sealed class Cell
    {
        public int X, Y;
        public bool Filled;
        public readonly bool[] Connect = new bool[4];
        public readonly Cell[] Next = new Cell[4];
        public int No = -1;
        public int Group = -1;
        public bool IsRaiseHeightCandidate;
        public bool IsShrinkWidthCandidate;
        public bool RaiseHeight;
        public bool ShrinkWidth;
        public bool TopTunnel;
        public bool IsJoinCandidate;
        public bool IsEdgeTunnelCandidate;
        public bool IsVoidTunnelCandidate;
        public bool IsSingleDeadEndCandidate;
        public bool IsDoubleDeadEndCandidate;
        public int SingleDeadEndDir;
        public int FinalX, FinalY, FinalW, FinalH;
    }

    private sealed class MazeBuilder
    {
        private readonly Random Rng;
        private readonly Cell[] Cells = new Cell[CellRows * CellCols];
        private readonly int[] TallRows = new int[CellCols];
        private readonly int[] NarrowCols = new int[CellRows];

        public MazeBuilder(Random Rng) { this.Rng = Rng; }

        public string[] Build()
        {
            Reset();
            Gen();
            if (!IsDesirable()) return null;
            SetUpScaleCoords();
            JoinWalls();
            if (!CreateTunnels()) return null;
            return GetTiles();
        }

        private void Shuffle<T>(List<T> List)
        {
            for (int I = List.Count - 1; I > 0; I--)
            {
                int J = Rng.Next(I + 1);
                (List[I], List[J]) = (List[J], List[I]);
            }
        }

        private Cell RandElement(List<Cell> List)
        {
            return List.Count == 0 ? null : List[Rng.Next(List.Count)];
        }

        private void Reset()
        {
            for (int I = 0; I < CellRows * CellCols; I++)
                Cells[I] = new Cell { X = I % CellCols, Y = I / CellCols };

            for (int I = 0; I < CellRows * CellCols; I++)
            {
                var C = Cells[I];
                if (C.X > 0) C.Next[Lt] = Cells[I - 1];
                if (C.X < CellCols - 1) C.Next[Rt] = Cells[I + 1];
                if (C.Y > 0) C.Next[Up] = Cells[I - CellCols];
                if (C.Y < CellRows - 1) C.Next[Dn] = Cells[I + CellCols];
            }

            // Ghost house: cells (0,3), (1,3), (0,4), (1,4)
            int Idx = 3 * CellCols;
            var G = Cells[Idx];
            G.Filled = true; G.Connect[Lt] = G.Connect[Rt] = G.Connect[Dn] = true;

            Idx++;
            G = Cells[Idx];
            G.Filled = true; G.Connect[Lt] = G.Connect[Dn] = true;

            Idx += CellCols - 1;
            G = Cells[Idx];
            G.Filled = true; G.Connect[Lt] = G.Connect[Up] = G.Connect[Rt] = true;

            Idx++;
            G = Cells[Idx];
            G.Filled = true; G.Connect[Up] = G.Connect[Lt] = true;

            for (int I = 0; I < CellCols; I++) TallRows[I] = CellRows;
            for (int I = 0; I < CellRows; I++) NarrowCols[I] = CellCols;
        }

        private List<Cell> GetLeftMostEmpty()
        {
            var R = new List<Cell>();
            for (int X = 0; X < CellCols; X++)
            {
                for (int Y = 0; Y < CellRows; Y++)
                {
                    var C = Cells[X + Y * CellCols];
                    if (!C.Filled) R.Add(C);
                }
                if (R.Count > 0) break;
            }
            return R;
        }

        private bool IsOpenCell(Cell C, int Dir, int PrevDir = -1, int Size = -1)
        {
            if (C.Y == 6 && C.X == 0 && Dir == Dn) return false;
            if (C.Y == 7 && C.X == 0 && Dir == Up) return false;
            if (Size == 2 && (Dir == PrevDir || (Dir + 2) % 4 == PrevDir)) return false;
            var Adj = C.Next[Dir];
            if (Adj != null && !Adj.Filled)
            {
                if (Adj.Next[Lt] != null && !Adj.Next[Lt].Filled) return false;
                return true;
            }
            return false;
        }

        private List<int> GetOpenDirs(Cell C, int PrevDir, int Size)
        {
            var R = new List<int>();
            for (int I = 0; I < 4; I++)
                if (IsOpenCell(C, I, PrevDir, Size)) R.Add(I);
            return R;
        }

        private void ConnectCell(Cell C, int Dir)
        {
            C.Connect[Dir] = true;
            C.Next[Dir].Connect[(Dir + 2) % 4] = true;
            if (C.X == 0 && Dir == Rt) C.Connect[Lt] = true;
        }

        private void FillCell(Cell C, ref int NumFilled, int Group)
        {
            C.Filled = true;
            C.No = NumFilled++;
            C.Group = Group;
        }

        private static double StopProb(int Size) => Size switch
        {
            <= 1 => 0, 2 => 0.10, 3 => 0.5, 4 => 0.75, _ => 1
        };

        private void Gen()
        {
            int NumFilled = 0;
            int NumGroups = 0;
            var SingleCount = new Dictionary<int, int> { { 0, 0 }, { CellRows - 1, 0 } };
            int LongPieces = 0;

            while (true)
            {
                var LeftCells = GetLeftMostEmpty();
                if (LeftCells.Count == 0) break;

                var FirstCell = LeftCells[Rng.Next(LeftCells.Count)];
                var Cur = FirstCell;
                FillCell(Cur, ref NumFilled, NumGroups);

                // Single cell at top/bottom boundary
                if (Cur.X < CellCols - 1 && SingleCount.ContainsKey(Cur.Y) && Rng.NextDouble() <= 0.35)
                {
                    if (SingleCount[Cur.Y] == 0)
                    {
                        Cur.Connect[Cur.Y == 0 ? Up : Dn] = true;
                        SingleCount[Cur.Y]++;
                        NumGroups++;
                        continue;
                    }
                }

                int Size = 1;

                if (Cur.X == CellCols - 1)
                {
                    Cur.Connect[Rt] = true;
                    Cur.IsRaiseHeightCandidate = true;
                }
                else
                {
                    Cell NewCell = null;
                    int Dir = -1;

                    while (Size < 5)
                    {
                        bool Stop = false;

                        // At size 2: try L-piece
                        if (Size == 2)
                        {
                            var C = FirstCell;
                            if (C.X > 0 && C.Connect[Rt] && C.Next[Rt]?.Next[Rt] != null)
                            {
                                if (LongPieces < 1 && Rng.NextDouble() <= 1.0)
                                {
                                    C = C.Next[Rt].Next[Rt];
                                    bool CanU = IsOpenCell(C, Up);
                                    bool CanD = IsOpenCell(C, Dn);
                                    int LD = -1;
                                    if (CanU && CanD) LD = Rng.Next(2) == 0 ? Up : Dn;
                                    else if (CanU) LD = Up;
                                    else if (CanD) LD = Dn;

                                    if (LD >= 0)
                                    {
                                        ConnectCell(C, Lt);
                                        FillCell(C, ref NumFilled, NumGroups);
                                        ConnectCell(C, LD);
                                        FillCell(C.Next[LD], ref NumFilled, NumGroups);
                                        LongPieces++;
                                        Size += 2;
                                        Stop = true;
                                    }
                                }
                            }
                        }

                        if (!Stop)
                        {
                            var Open = GetOpenDirs(Cur, Dir, Size);
                            if (Open.Count == 0 && Size == 2)
                            {
                                Cur = NewCell;
                                Open = GetOpenDirs(Cur, Dir, Size);
                            }

                            if (Open.Count == 0)
                            {
                                Stop = true;
                            }
                            else
                            {
                                Dir = Open[Rng.Next(Open.Count)];
                                NewCell = Cur.Next[Dir];
                                ConnectCell(Cur, Dir);
                                FillCell(NewCell, ref NumFilled, NumGroups);
                                Size++;

                                if (FirstCell.X == 0 && Size == 3) Stop = true;
                                if (!Stop && Rng.NextDouble() <= StopProb(Size)) Stop = true;
                            }
                        }

                        if (Stop)
                        {
                            if (Size == 2)
                            {
                                // Attach vertical pair at right edge to wall
                                var C = FirstCell;
                                if (C.X == CellCols - 1)
                                {
                                    if (C.Connect[Up]) C = C.Next[Up];
                                    C.Connect[Rt] = true;
                                    C.Next[Dn].Connect[Rt] = true;
                                }
                            }
                            else if (Size == 3 || Size == 4)
                            {
                                // Try long leg extension
                                if (LongPieces < 1 && FirstCell.X > 0 && Rng.NextDouble() <= 0.5)
                                {
                                    var Dirs = new List<int>();
                                    for (int I = 0; I < 4; I++)
                                        if (Cur.Connect[I] && IsOpenCell(Cur.Next[I], I))
                                            Dirs.Add(I);
                                    if (Dirs.Count > 0)
                                    {
                                        int ED = Dirs[Rng.Next(Dirs.Count)];
                                        var EC = Cur.Next[ED];
                                        ConnectCell(EC, ED);
                                        FillCell(EC.Next[ED], ref NumFilled, NumGroups);
                                        LongPieces++;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
                NumGroups++;
            }
            SetResizeCandidates();
        }

        private void SetResizeCandidates()
        {
            for (int I = 0; I < CellRows * CellCols; I++)
            {
                var C = Cells[I];
                var Q = C.Connect;

                if ((C.X == 0 || !Q[Lt]) && (C.X == CellCols - 1 || !Q[Rt]) && Q[Up] != Q[Dn])
                    C.IsRaiseHeightCandidate = true;

                var C2 = C.Next[Rt];
                if (C2 != null)
                {
                    var Q2 = C2.Connect;
                    if (((C.X == 0 || !Q[Lt]) && !Q[Up] && !Q[Dn]) &&
                        ((C2.X == CellCols - 1 || !Q2[Rt]) && !Q2[Up] && !Q2[Dn]))
                        C.IsRaiseHeightCandidate = C2.IsRaiseHeightCandidate = true;
                }

                if (C.X == CellCols - 1 && Q[Rt])
                    C.IsShrinkWidthCandidate = true;

                if ((C.Y == 0 || !Q[Up]) && (C.Y == CellRows - 1 || !Q[Dn]) && Q[Lt] != Q[Rt])
                    C.IsShrinkWidthCandidate = true;
            }
        }

        private static bool IsCross(Cell C) =>
            C.Connect[Up] && C.Connect[Rt] && C.Connect[Dn] && C.Connect[Lt];

        private bool IsHori(int X, int Y)
        {
            var Q1 = Cells[X + Y * CellCols].Connect;
            var Q2 = Cells[X + 1 + Y * CellCols].Connect;
            return !Q1[Up] && !Q1[Dn] && (X == 0 || !Q1[Lt]) && Q1[Rt] &&
                   !Q2[Up] && !Q2[Dn] && Q2[Lt] && !Q2[Rt];
        }

        private bool IsVert(int X, int Y)
        {
            var Q1 = Cells[X + Y * CellCols].Connect;
            var Q2 = Cells[X + (Y + 1) * CellCols].Connect;
            if (X == CellCols - 1)
                return !Q1[Lt] && !Q1[Up] && !Q1[Dn] && !Q2[Lt] && !Q2[Up] && !Q2[Dn];
            return !Q1[Lt] && !Q1[Rt] && !Q1[Up] && Q1[Dn] &&
                   !Q2[Lt] && !Q2[Rt] && Q2[Up] && !Q2[Dn];
        }

        private bool IsDesirable()
        {
            // Solid corners
            if (Cells[4].Connect[Up] || Cells[4].Connect[Rt]) return false;
            if (Cells[CellRows * CellCols - 1].Connect[Dn] || Cells[CellRows * CellCols - 1].Connect[Rt]) return false;

            // Fix stacked 2-cell pieces
            for (int Y = 0; Y < CellRows - 1; Y++)
            {
                for (int X = 0; X < CellCols - 1; X++)
                {
                    if ((IsHori(X, Y) && IsHori(X, Y + 1)) || (IsVert(X, Y) && IsVert(X + 1, Y)))
                    {
                        if (X == 0) return false;
                        int G = Cells[X + Y * CellCols].Group;
                        Cells[X + Y * CellCols].Connect[Dn] = Cells[X + Y * CellCols].Connect[Rt] = true;
                        Cells[X + Y * CellCols].Group = G;
                        Cells[X + 1 + Y * CellCols].Connect[Dn] = Cells[X + 1 + Y * CellCols].Connect[Lt] = true;
                        Cells[X + 1 + Y * CellCols].Group = G;
                        Cells[X + (Y + 1) * CellCols].Connect[Up] = Cells[X + (Y + 1) * CellCols].Connect[Rt] = true;
                        Cells[X + (Y + 1) * CellCols].Group = G;
                        Cells[X + 1 + (Y + 1) * CellCols].Connect[Up] = Cells[X + 1 + (Y + 1) * CellCols].Connect[Lt] = true;
                        Cells[X + 1 + (Y + 1) * CellCols].Group = G;
                    }
                }
            }

            if (!ChooseTallRows()) return false;
            if (!ChooseNarrowCols()) return false;
            return true;
        }

        private bool ChooseTallRows()
        {
            for (int Y = 0; Y < 3; Y++)
            {
                var C = Cells[Y * CellCols];
                if (C.IsRaiseHeightCandidate && CanRaiseHeight(0, Y))
                {
                    C.RaiseHeight = true;
                    TallRows[C.X] = C.Y;
                    return true;
                }
            }
            return false;
        }

        private bool CanRaiseHeight(int X, int Y)
        {
            if (X == CellCols - 1) return true;

            Cell C = null, C2 = null;
            for (int Y0 = Y; Y0 >= 0; Y0--)
            {
                C = Cells[X + Y0 * CellCols];
                C2 = C.Next[Rt];
                if ((!C.Connect[Up] || IsCross(C)) && (!C2.Connect[Up] || IsCross(C2)))
                    break;
            }

            var Cands = new List<Cell>();
            for (; C2 != null; C2 = C2.Next[Dn])
            {
                if (C2.IsRaiseHeightCandidate) Cands.Add(C2);
                if ((!C2.Connect[Dn] || IsCross(C2)) &&
                    C2.Next[Lt] != null && (!C2.Next[Lt].Connect[Dn] || IsCross(C2.Next[Lt])))
                    break;
            }

            Shuffle(Cands);
            foreach (var Cd in Cands)
            {
                if (CanRaiseHeight(Cd.X, Cd.Y))
                {
                    Cd.RaiseHeight = true;
                    TallRows[Cd.X] = Cd.Y;
                    return true;
                }
            }
            return false;
        }

        private bool ChooseNarrowCols()
        {
            for (int X = CellCols - 1; X >= 0; X--)
            {
                var C = Cells[X];
                if (C.IsShrinkWidthCandidate && CanShrinkWidth(X, 0))
                {
                    C.ShrinkWidth = true;
                    NarrowCols[C.Y] = C.X;
                    return true;
                }
            }
            return false;
        }

        private bool CanShrinkWidth(int X, int Y)
        {
            if (Y == CellRows - 1) return true;

            Cell C = null, C2 = null;
            for (int X0 = X; X0 < CellCols; X0++)
            {
                C = Cells[X0 + Y * CellCols];
                C2 = C.Next[Dn];
                if ((!C.Connect[Rt] || IsCross(C)) && (!C2.Connect[Rt] || IsCross(C2)))
                    break;
            }

            var Cands = new List<Cell>();
            for (; C2 != null; C2 = C2.Next[Lt])
            {
                if (C2.IsShrinkWidthCandidate) Cands.Add(C2);
                if ((!C2.Connect[Lt] || IsCross(C2)) &&
                    C2.Next[Up] != null && (!C2.Next[Up].Connect[Lt] || IsCross(C2.Next[Up])))
                    break;
            }

            Shuffle(Cands);
            foreach (var Cd in Cands)
            {
                if (CanShrinkWidth(Cd.X, Cd.Y))
                {
                    Cd.ShrinkWidth = true;
                    NarrowCols[Cd.Y] = Cd.X;
                    return true;
                }
            }
            return false;
        }

        private void SetUpScaleCoords()
        {
            for (int I = 0; I < CellRows * CellCols; I++)
            {
                var C = Cells[I];
                C.FinalX = C.X * 3 - (NarrowCols[C.Y] < C.X ? 1 : 0);
                C.FinalY = C.Y * 3 + (TallRows[C.X] < C.Y ? 1 : 0);
                C.FinalW = C.ShrinkWidth ? 2 : 3;
                C.FinalH = C.RaiseHeight ? 4 : 3;
            }
        }

        private void JoinWalls()
        {
            // Join to top boundary
            for (int X = 0; X < CellCols; X++)
            {
                var C = Cells[X];
                if (!C.Connect[Lt] && !C.Connect[Rt] && !C.Connect[Up] &&
                    (!C.Connect[Dn] || !C.Next[Dn].Connect[Dn]))
                {
                    if ((C.Next[Lt] == null || !C.Next[Lt].Connect[Up]) &&
                        (C.Next[Rt] != null && !C.Next[Rt].Connect[Up]))
                    {
                        if (!(C.Next[Dn] != null && C.Next[Dn].Connect[Rt] &&
                              C.Next[Dn].Next[Rt] != null && C.Next[Dn].Next[Rt].Connect[Rt]))
                        {
                            C.IsJoinCandidate = true;
                            if (Rng.NextDouble() <= 0.25) C.Connect[Up] = true;
                        }
                    }
                }
            }

            // Join to bottom boundary
            for (int X = 0; X < CellCols; X++)
            {
                var C = Cells[X + (CellRows - 1) * CellCols];
                if (!C.Connect[Lt] && !C.Connect[Rt] && !C.Connect[Dn] &&
                    (!C.Connect[Up] || !C.Next[Up].Connect[Up]))
                {
                    if ((C.Next[Lt] == null || !C.Next[Lt].Connect[Dn]) &&
                        (C.Next[Rt] != null && !C.Next[Rt].Connect[Dn]))
                    {
                        if (!(C.Next[Up] != null && C.Next[Up].Connect[Rt] &&
                              C.Next[Up].Next[Rt] != null && C.Next[Up].Next[Rt].Connect[Rt]))
                        {
                            C.IsJoinCandidate = true;
                            if (Rng.NextDouble() <= 0.25) C.Connect[Dn] = true;
                        }
                    }
                }
            }

            // Join to right boundary
            for (int Y = 1; Y < CellRows - 1; Y++)
            {
                var C = Cells[CellCols - 1 + Y * CellCols];
                if (C.RaiseHeight) continue;
                if (!C.Connect[Rt] && !C.Connect[Up] && !C.Connect[Dn] &&
                    !C.Next[Up].Connect[Rt] && !C.Next[Dn].Connect[Rt])
                {
                    if (C.Connect[Lt])
                    {
                        var C2 = C.Next[Lt];
                        if (!C2.Connect[Up] && !C2.Connect[Dn] && !C2.Connect[Lt])
                        {
                            C.IsJoinCandidate = true;
                            if (Rng.NextDouble() <= 0.5) C.Connect[Rt] = true;
                        }
                    }
                }
            }
        }

        private bool CreateTunnels()
        {
            var SingleDead = new List<Cell>();
            var TopSingleDead = new List<Cell>();
            var BotSingleDead = new List<Cell>();
            var VoidTunnel = new List<Cell>();
            var TopVoid = new List<Cell>();
            var BotVoid = new List<Cell>();
            var EdgeTunnel = new List<Cell>();
            var TopEdge = new List<Cell>();
            var BotEdge = new List<Cell>();
            var DoubleDead = new List<Cell>();

            for (int Y = 0; Y < CellRows; Y++)
            {
                var C = Cells[CellCols - 1 + Y * CellCols];
                if (C.Connect[Up]) continue;

                if (C.Y > 1 && C.Y < CellRows - 2)
                {
                    C.IsEdgeTunnelCandidate = true;
                    EdgeTunnel.Add(C);
                    if (C.Y <= 2) TopEdge.Add(C);
                    else if (C.Y >= 5) BotEdge.Add(C);
                }

                bool UpDead = C.Next[Up] == null || C.Next[Up].Connect[Rt];
                bool DnDead = C.Next[Dn] == null || C.Next[Dn].Connect[Rt];

                if (C.Connect[Rt])
                {
                    if (UpDead)
                    {
                        C.IsVoidTunnelCandidate = true;
                        VoidTunnel.Add(C);
                        if (C.Y <= 2) TopVoid.Add(C);
                        else if (C.Y >= 6) BotVoid.Add(C);
                    }
                }
                else
                {
                    if (C.Connect[Dn]) continue;
                    if (UpDead != DnDead)
                    {
                        if (!C.RaiseHeight && Y < CellRows - 1 && C.Next[Lt] != null && !C.Next[Lt].Connect[Lt])
                        {
                            SingleDead.Add(C);
                            C.IsSingleDeadEndCandidate = true;
                            C.SingleDeadEndDir = UpDead ? Up : Dn;
                            int Off = UpDead ? 1 : 0;
                            if (C.Y <= 1 + Off) TopSingleDead.Add(C);
                            else if (C.Y >= 5 + Off) BotSingleDead.Add(C);
                        }
                    }
                    else if (UpDead && DnDead)
                    {
                        if (Y > 0 && Y < CellRows - 1 && C.Next[Lt] != null)
                        {
                            if (C.Next[Lt].Connect[Up] && C.Next[Lt].Connect[Dn])
                            {
                                C.IsDoubleDeadEndCandidate = true;
                                if (C.Y >= 2 && C.Y <= 5) DoubleDead.Add(C);
                            }
                        }
                    }
                }
            }

            // Choose tunnels
            int Desired = Rng.NextDouble() <= 0.45 ? 2 : 1;
            Cell Pick;

            if (Desired == 1)
            {
                if ((Pick = RandElement(VoidTunnel)) != null) Pick.TopTunnel = true;
                else if ((Pick = RandElement(SingleDead)) != null) SelSingleDead(Pick);
                else if ((Pick = RandElement(EdgeTunnel)) != null) Pick.TopTunnel = true;
                else return false;
            }
            else
            {
                if ((Pick = RandElement(DoubleDead)) != null)
                {
                    Pick.Connect[Rt] = true;
                    Pick.TopTunnel = true;
                    Pick.Next[Dn].TopTunnel = true;
                }
                else
                {
                    int Created = 1;
                    if ((Pick = RandElement(TopVoid)) != null) Pick.TopTunnel = true;
                    else if ((Pick = RandElement(TopSingleDead)) != null) SelSingleDead(Pick);
                    else if ((Pick = RandElement(TopEdge)) != null) Pick.TopTunnel = true;
                    else Created = 0;

                    if ((Pick = RandElement(BotVoid)) != null) Pick.TopTunnel = true;
                    else if ((Pick = RandElement(BotSingleDead)) != null) SelSingleDead(Pick);
                    else if ((Pick = RandElement(BotEdge)) != null) Pick.TopTunnel = true;
                    else if (Created == 0) return false;
                }
            }

            // No straight-through horizontal path
            for (int Y = 0; Y < CellRows; Y++)
            {
                var C = Cells[CellCols - 1 + Y * CellCols];
                if (!C.TopTunnel) continue;
                bool Straight = true;
                int TopY = C.FinalY;
                var W = C;
                while (W.Next[Lt] != null)
                {
                    W = W.Next[Lt];
                    if (!W.Connect[Up] && W.FinalY == TopY) continue;
                    Straight = false;
                    break;
                }
                if (Straight) return false;
            }

            // Clear unused void tunnels
            foreach (var Vc in VoidTunnel)
            {
                if (!Vc.TopTunnel)
                {
                    ReplaceGroup(Vc.Group, Vc.Next[Up].Group);
                    Vc.Connect[Up] = true;
                    Vc.Next[Up].Connect[Dn] = true;
                }
            }

            return true;
        }

        private void SelSingleDead(Cell C)
        {
            C.Connect[Rt] = true;
            if (C.SingleDeadEndDir == Up) C.TopTunnel = true;
            else C.Next[Dn].TopTunnel = true;
        }

        private void ReplaceGroup(int OldG, int NewG)
        {
            for (int I = 0; I < CellRows * CellCols; I++)
                if (Cells[I].Group == OldG) Cells[I].Group = NewG;
        }

        private string[] GetTiles()
        {
            var Tiles = new char[SubRows * FullCols];
            var TC = new Cell[SubRows * SubCols];

            for (int I = 0; I < Tiles.Length; I++) Tiles[I] = '_';

            void ST(int X, int Y, char V)
            {
                if (X < 0 || X > SubCols - 1 || Y < 0 || Y > SubRows - 1) return;
                int A = X - 2;
                int Ia = MidCols + A + Y * FullCols;
                int Ib = MidCols - 1 - A + Y * FullCols;
                if (Ia >= 0 && Ia < Tiles.Length) Tiles[Ia] = V;
                if (Ib >= 0 && Ib < Tiles.Length) Tiles[Ib] = V;
            }

            char GT(int X, int Y)
            {
                if (X < 0 || X > SubCols - 1 || Y < 0 || Y > SubRows - 1) return '\0';
                int Idx = MidCols + (X - 2) + Y * FullCols;
                return (Idx >= 0 && Idx < Tiles.Length) ? Tiles[Idx] : '\0';
            }

            void STC(int X, int Y, Cell C)
            {
                if (X < 0 || X > SubCols - 1 || Y < 0 || Y > SubRows - 1) return;
                int Idx = X + Y * SubCols;
                if (Idx >= 0 && Idx < TC.Length) TC[Idx] = C;
            }

            Cell GTC(int X, int Y)
            {
                if (X < 0 || X > SubCols - 1 || Y < 0 || Y > SubRows - 1) return null;
                int Idx = X + Y * SubCols;
                return (Idx >= 0 && Idx < TC.Length) ? TC[Idx] : null;
            }

            // Map cells to tile positions
            for (int I = 0; I < CellRows * CellCols; I++)
            {
                var C = Cells[I];
                for (int X0 = 0; X0 < C.FinalW; X0++)
                    for (int Y0 = 0; Y0 < C.FinalH; Y0++)
                        STC(C.FinalX + X0, C.FinalY + 1 + Y0, C);
            }

            // Set path tiles
            for (int Y = 0; Y < SubRows; Y++)
            {
                for (int X = 0; X < SubCols; X++)
                {
                    var C = GTC(X, Y);
                    var Cl = GTC(X - 1, Y);
                    var Cu = GTC(X, Y - 1);

                    if (C != null)
                    {
                        if ((Cl != null && C.Group != Cl.Group) ||
                            (Cu != null && C.Group != Cu.Group) ||
                            (Cu == null && !C.Connect[Up]))
                            ST(X, Y, '.');
                    }
                    else
                    {
                        if ((Cl != null && (!Cl.Connect[Rt] || GT(X - 1, Y) == '.')) ||
                            (Cu != null && (!Cu.Connect[Dn] || GT(X, Y - 1) == '.')))
                            ST(X, Y, '.');
                    }

                    if (GT(X - 1, Y) == '.' && GT(X, Y - 1) == '.' && GT(X - 1, Y - 1) == '_')
                        ST(X, Y, '.');
                }
            }

            // Extend tunnels
            for (var C = Cells[CellCols - 1]; C != null; C = C.Next[Dn])
            {
                if (C.TopTunnel)
                {
                    int Y = C.FinalY + 1;
                    ST(SubCols - 1, Y, '.');
                    ST(SubCols - 2, Y, '.');
                }
            }

            // Fill in walls
            for (int Y = 0; Y < SubRows; Y++)
            {
                for (int X = 0; X < SubCols; X++)
                {
                    if (GT(X, Y) != '.' &&
                        (GT(X - 1, Y) == '.' || GT(X, Y - 1) == '.' ||
                         GT(X + 1, Y) == '.' || GT(X, Y + 1) == '.' ||
                         GT(X - 1, Y - 1) == '.' || GT(X + 1, Y - 1) == '.' ||
                         GT(X + 1, Y + 1) == '.' || GT(X - 1, Y + 1) == '.'))
                        ST(X, Y, '|');
                }
            }

            // Stamp ghost house (cols 9-18, rows 11-17) matching fallback layout
            void Set(int X, int Y, char V) { Tiles[X + Y * FullCols] = V; }

            // Row 11: corridor above
            for (int X = 9; X <= 18; X++) Set(X, 11, '.');
            // Row 12: top wall + door
            Set(9, 12, '.'); Set(18, 12, '.');
            for (int X = 10; X <= 12; X++) Set(X, 12, '|');
            Set(13, 12, '.'); Set(14, 12, '.');
            for (int X = 15; X <= 17; X++) Set(X, 12, '|');
            // Rows 13-15: interior
            for (int Y = 13; Y <= 15; Y++)
            {
                Set(9, Y, '.'); Set(10, Y, '|');
                for (int X = 11; X <= 16; X++) Set(X, Y, '.');
                Set(17, Y, '|'); Set(18, Y, '.');
            }
            // Row 16: bottom wall
            Set(9, 16, '.'); Set(18, 16, '.');
            for (int X = 10; X <= 17; X++) Set(X, 16, '|');
            // Row 17: corridor below
            for (int X = 9; X <= 18; X++) Set(X, 17, '.');

            // Convert to output
            var Layout = new string[SubRows];
            for (int Y = 0; Y < SubRows; Y++)
            {
                var Row = new char[FullCols];
                for (int X = 0; X < FullCols; X++)
                {
                    char T = Tiles[X + Y * FullCols];
                    Row[X] = (T == '.') ? '0' : '1';
                }
                Layout[Y] = new string(Row);
            }
            return Layout;
        }
    }

    private static string[] FallbackLayout() =>
    [
        "1111111111111111111111111111",
        "1000000000000110000000000001",
        "1011110111110110111110111101",
        "1011110111110110111110111101",
        "1011110111110110111110111101",
        "1000000000000000000000000001",
        "1011110110111111110110111101",
        "1011110110111111110110111101",
        "1000000110000110000110000001",
        "1111110111110110111110111111",
        "1111110111110110111110111111",
        "1111110110000000000110111111",
        "1111110110111001110110111111",
        "1111110110100000010110111111",
        "0000000000100000010000000000",
        "1111110110100000010110111111",
        "1111110110111111110110111111",
        "1111110110000000000110111111",
        "1111110110111111110110111111",
        "1111110110111111110110111111",
        "1000000000000110000000000001",
        "1011110111110110111110111101",
        "1011110111110110111110111101",
        "1000110000000000000000110001",
        "1110110110111111110110110111",
        "1110110110111111110110110111",
        "1000000110000110000110000001",
        "1011111111110110111111111101",
        "1011111111110110111111111101",
        "1000000000000000000000000001",
        "1111111111111111111111111111",
    ];
}
