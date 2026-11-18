public sealed record GameSnapshot(
    string[] Board,
    string ActiveColor,
    bool InCheck,
    string? Status,
    string? LastMove,
    string[] Moves,
    string[] LegalMoves);

public sealed class ChessGame
{
    private readonly object gate = new();
    private readonly char[,] board = new char[8, 8];
    private readonly List<string> moveHistory = [];
    private bool whiteToMove = true;
    private bool whiteKingSide = true;
    private bool whiteQueenSide = true;
    private bool blackKingSide = true;
    private bool blackQueenSide = true;
    private (int X, int Y)? enPassantTarget;
    private int halfmoveClock;

    public ChessGame()
    {
        const string backRank = "rnbqkbnr";
        for (var x = 0; x < 8; x++)
        {
            board[x, 0] = backRank[x];
            board[x, 1] = 'p';
            board[x, 6] = 'P';
            board[x, 7] = char.ToUpperInvariant(backRank[x]);
        }
    }

    private ChessGame(ChessGame source)
    {
        Array.Copy(source.board, board, source.board.Length);
        moveHistory = [.. source.moveHistory];
        whiteToMove = source.whiteToMove;
        whiteKingSide = source.whiteKingSide;
        whiteQueenSide = source.whiteQueenSide;
        blackKingSide = source.blackKingSide;
        blackQueenSide = source.blackQueenSide;
        enPassantTarget = source.enPassantTarget;
        halfmoveClock = source.halfmoveClock;
    }

    public GameSnapshot GetSnapshot()
    {
        lock (gate)
        {
            var legalMoves = GetLegalMoves(whiteToMove);
            return new GameSnapshot(
                Enumerable.Range(0, 8)
                    .Select(y => new string(Enumerable.Range(0, 8)
                        .Select(x => board[x, y] == '\0' ? '.' : board[x, y]).ToArray()))
                    .ToArray(),
                whiteToMove ? "white" : "black",
                IsKingInCheck(whiteToMove),
                GetEndStatus(legalMoves),
                moveHistory.Count == 0 ? null : moveHistory[^1],
                [.. moveHistory],
                legalMoves.Select(move => ToSquare(move.FromX, move.FromY) + ToSquare(move.ToX, move.ToY)).ToArray());
        }
    }

    public bool TryMove(string from, string to, out string error)
    {
        lock (gate)
        {
            if (!TryParseSquare(from, out var fromX, out var fromY)
                || !TryParseSquare(to, out var toX, out var toY))
            {
                error = "Use valid squares such as e2 and e4.";
                return false;
            }

            var legalMoves = GetLegalMoves(whiteToMove);
            if (GetEndStatus(legalMoves) is not null)
            {
                error = "This game is over. Start a new game to play again.";
                return false;
            }

            var move = legalMoves.FirstOrDefault(candidate =>
                candidate.FromX == fromX && candidate.FromY == fromY
                && candidate.ToX == toX && candidate.ToY == toY);

            if (move == default)
            {
                error = "That move is not legal.";
                return false;
            }

            ApplyMove(move);
            moveHistory.Add(ToSquare(fromX, fromY) + ToSquare(toX, toY));
            error = "";
            return true;
        }
    }

    private string? GetEndStatus(List<Move> legalMoves)
    {
        var inCheck = IsKingInCheck(whiteToMove);
        if (legalMoves.Count == 0)
            return inCheck
                ? $"Checkmate! {(whiteToMove ? "Black" : "White")} wins."
                : "Stalemate — draw.";

        return halfmoveClock >= 100 ? "Draw by the fifty-move rule." : null;
    }

    private List<Move> GetLegalMoves(bool forWhite)
    {
        var legalMoves = new List<Move>();
        foreach (var move in GetPseudoLegalMoves(forWhite))
        {
            var position = new ChessGame(this);
            position.ApplyMove(move);
            if (!position.IsKingInCheck(forWhite))
                legalMoves.Add(move);
        }

        return legalMoves;
    }

    private List<Move> GetPseudoLegalMoves(bool forWhite)
    {
        var moves = new List<Move>();
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var piece = board[x, y];
            if (piece == '\0' || char.IsUpper(piece) != forWhite)
                continue;

            switch (char.ToLowerInvariant(piece))
            {
                case 'p':
                    AddPawnMoves(moves, x, y, forWhite);
                    break;
                case 'n':
                    foreach (var (dx, dy) in new[] { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) })
                        AddStepIfAvailable(moves, x, y, x + dx, y + dy, forWhite);
                    break;
                case 'b':
                    AddSlidingMoves(moves, x, y, forWhite, [(-1, -1), (1, -1), (-1, 1), (1, 1)]);
                    break;
                case 'r':
                    AddSlidingMoves(moves, x, y, forWhite, [(-1, 0), (1, 0), (0, -1), (0, 1)]);
                    break;
                case 'q':
                    AddSlidingMoves(moves, x, y, forWhite,
                        [(-1, -1), (1, -1), (-1, 1), (1, 1), (-1, 0), (1, 0), (0, -1), (0, 1)]);
                    break;
                case 'k':
                    for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0)
                            AddStepIfAvailable(moves, x, y, x + dx, y + dy, forWhite);
                    AddCastlingMoves(moves, x, y, forWhite);
                    break;
            }
        }

        return moves;
    }

    private void AddPawnMoves(List<Move> moves, int x, int y, bool forWhite)
    {
        var direction = forWhite ? -1 : 1;
        var nextY = y + direction;
        if (Inside(x, nextY) && board[x, nextY] == '\0')
        {
            moves.Add(new Move(x, y, x, nextY));
            var startY = forWhite ? 6 : 1;
            if (y == startY && board[x, nextY + direction] == '\0')
                moves.Add(new Move(x, y, x, nextY + direction));
        }

        foreach (var dx in new[] { -1, 1 })
        {
            var targetX = x + dx;
            if (!Inside(targetX, nextY))
                continue;

            var target = board[targetX, nextY];
            if (target != '\0' && char.IsUpper(target) != forWhite && char.ToLowerInvariant(target) != 'k')
                moves.Add(new Move(x, y, targetX, nextY));
            else if (enPassantTarget == (targetX, nextY))
                moves.Add(new Move(x, y, targetX, nextY));
        }
    }

    private void AddStepIfAvailable(List<Move> moves, int fromX, int fromY, int toX, int toY, bool forWhite)
    {
        if (!Inside(toX, toY))
            return;

        var target = board[toX, toY];
        if (target == '\0' || (char.IsUpper(target) != forWhite && char.ToLowerInvariant(target) != 'k'))
            moves.Add(new Move(fromX, fromY, toX, toY));
    }

    private void AddSlidingMoves(List<Move> moves, int x, int y, bool forWhite, (int X, int Y)[] directions)
    {
        foreach (var (dx, dy) in directions)
        {
            var toX = x + dx;
            var toY = y + dy;
            while (Inside(toX, toY))
            {
                var target = board[toX, toY];
                if (target == '\0')
                    moves.Add(new Move(x, y, toX, toY));
                else
                {
                    if (char.IsUpper(target) != forWhite && char.ToLowerInvariant(target) != 'k')
                        moves.Add(new Move(x, y, toX, toY));
                    break;
                }

                toX += dx;
                toY += dy;
            }
        }
    }

    private void AddCastlingMoves(List<Move> moves, int x, int y, bool forWhite)
    {
        var homeY = forWhite ? 7 : 0;
        if (x != 4 || y != homeY || IsSquareAttacked(4, homeY, !forWhite))
            return;

        var kingSide = forWhite ? whiteKingSide : blackKingSide;
        var queenSide = forWhite ? whiteQueenSide : blackQueenSide;
        var rook = forWhite ? 'R' : 'r';

        if (kingSide && board[7, homeY] == rook && board[5, homeY] == '\0' && board[6, homeY] == '\0'
            && !IsSquareAttacked(5, homeY, !forWhite) && !IsSquareAttacked(6, homeY, !forWhite))
            moves.Add(new Move(4, homeY, 6, homeY));

        if (queenSide && board[0, homeY] == rook && board[1, homeY] == '\0'
            && board[2, homeY] == '\0' && board[3, homeY] == '\0'
            && !IsSquareAttacked(3, homeY, !forWhite) && !IsSquareAttacked(2, homeY, !forWhite))
            moves.Add(new Move(4, homeY, 2, homeY));
    }

    private bool IsKingInCheck(bool forWhite)
    {
        var king = forWhite ? 'K' : 'k';
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            if (board[x, y] == king)
                return IsSquareAttacked(x, y, !forWhite);
        return true;
    }

    private bool IsSquareAttacked(int x, int y, bool byWhite)
    {
        var pawnY = y + (byWhite ? 1 : -1);
        foreach (var pawnX in new[] { x - 1, x + 1 })
            if (Inside(pawnX, pawnY) && board[pawnX, pawnY] == (byWhite ? 'P' : 'p'))
                return true;

        foreach (var (dx, dy) in new[] { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) })
        {
            var knightX = x + dx;
            var knightY = y + dy;
            if (Inside(knightX, knightY) && board[knightX, knightY] == (byWhite ? 'N' : 'n'))
                return true;
        }

        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
            if ((dx != 0 || dy != 0) && Inside(x + dx, y + dy)
                && board[x + dx, y + dy] == (byWhite ? 'K' : 'k'))
                return true;

        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var scanX = x + dx;
            var scanY = y + dy;
            var distance = 1;
            while (Inside(scanX, scanY))
            {
                var piece = board[scanX, scanY];
                if (piece != '\0')
                {
                    if (char.IsUpper(piece) == byWhite)
                    {
                        var type = char.ToLowerInvariant(piece);
                        if (type == 'q' || (distance == 1 && type == 'k')
                            || (dx == 0 || dy == 0 ? type == 'r' : type == 'b'))
                            return true;
                    }
                    break;
                }

                scanX += dx;
                scanY += dy;
                distance++;
            }
        }

        return false;
    }

    private void ApplyMove(Move move)
    {
        var piece = board[move.FromX, move.FromY];
        var target = board[move.ToX, move.ToY];
        var isPawn = char.ToLowerInvariant(piece) == 'p';
        var isEnPassant = isPawn && move.FromX != move.ToX && target == '\0';

        if (isEnPassant)
            board[move.ToX, move.ToY + (char.IsUpper(piece) ? 1 : -1)] = '\0';

        board[move.FromX, move.FromY] = '\0';
        board[move.ToX, move.ToY] = isPawn && (move.ToY == 0 || move.ToY == 7)
            ? (char.IsUpper(piece) ? 'Q' : 'q')
            : piece;

        if (char.ToLowerInvariant(piece) == 'k' && Math.Abs(move.ToX - move.FromX) == 2)
        {
            var rookFromX = move.ToX > move.FromX ? 7 : 0;
            var rookToX = move.ToX > move.FromX ? 5 : 3;
            board[rookToX, move.FromY] = board[rookFromX, move.FromY];
            board[rookFromX, move.FromY] = '\0';
        }

        UpdateCastlingRights(piece, move.FromX, move.FromY, target, move.ToX, move.ToY);
        enPassantTarget = isPawn && Math.Abs(move.ToY - move.FromY) == 2
            ? (move.FromX, (move.FromY + move.ToY) / 2)
            : null;
        halfmoveClock = isPawn || target != '\0' || isEnPassant ? 0 : halfmoveClock + 1;
        whiteToMove = !whiteToMove;
    }

    private void UpdateCastlingRights(char piece, int fromX, int fromY, char target, int toX, int toY)
    {
        if (piece == 'K') whiteKingSide = whiteQueenSide = false;
        if (piece == 'k') blackKingSide = blackQueenSide = false;
        if (piece == 'R' && fromY == 7 && fromX == 0) whiteQueenSide = false;
        if (piece == 'R' && fromY == 7 && fromX == 7) whiteKingSide = false;
        if (piece == 'r' && fromY == 0 && fromX == 0) blackQueenSide = false;
        if (piece == 'r' && fromY == 0 && fromX == 7) blackKingSide = false;
        if (target == 'R' && toY == 7 && toX == 0) whiteQueenSide = false;
        if (target == 'R' && toY == 7 && toX == 7) whiteKingSide = false;
        if (target == 'r' && toY == 0 && toX == 0) blackQueenSide = false;
        if (target == 'r' && toY == 0 && toX == 7) blackKingSide = false;
    }

    private static bool TryParseSquare(string square, out int x, out int y)
    {
        if (square.Length == 2 && square[0] is >= 'a' and <= 'h' && square[1] is >= '1' and <= '8')
        {
            x = square[0] - 'a';
            y = 8 - (square[1] - '0');
            return true;
        }

        x = y = -1;
        return false;
    }

    private static string ToSquare(int x, int y) => $"{(char)('a' + x)}{8 - y}";
    private static bool Inside(int x, int y) => x is >= 0 and < 8 && y is >= 0 and < 8;

    private readonly record struct Move(int FromX, int FromY, int ToX, int ToY);
}
