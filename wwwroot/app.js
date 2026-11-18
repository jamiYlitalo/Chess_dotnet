const pieceSymbols = {
  K: "♔", Q: "♕", R: "♖", B: "♗", N: "♘", P: "♙",
  k: "♚", q: "♛", r: "♜", b: "♝", n: "♞", p: "♟"
};

const boardElement = document.querySelector("#board");
const turnLabel = document.querySelector("#turn-label");
const turnMark = document.querySelector("#turn-mark");
const messageElement = document.querySelector("#game-message");
const moveList = document.querySelector("#move-list");
const moveCount = document.querySelector("#move-count");
const emptyHistory = document.querySelector("#empty-history");
const newGameButton = document.querySelector("#new-game");

let gameId = localStorage.getItem("little-chess-game");
let gameState = null;
let selectedSquare = null;
let legalMoves = [];
let flipped = false;
let busy = false;

document.querySelector("#new-game").addEventListener("click", startNewGame);
document.querySelector("#flip-board").addEventListener("click", () => {
  flipped = !flipped;
  renderBoard();
});

function squareName(file, rank) {
  return `${String.fromCharCode(97 + file)}${8 - rank}`;
}

function getLegalDestinations(square) {
  return legalMoves
    .filter(move => move.startsWith(square))
    .map(move => move.slice(2, 4));
}

function renderBoard() {
  if (!gameState) return;
  const rows = [...Array(8).keys()];
  const files = [...Array(8).keys()];
  if (flipped) {
    rows.reverse();
    files.reverse();
  }

  const destinations = selectedSquare ? getLegalDestinations(selectedSquare) : [];
  const lastMove = gameState.lastMove
    ? [gameState.lastMove.slice(0, 2), gameState.lastMove.slice(2, 4)]
    : [];
  boardElement.replaceChildren();

  for (const rank of rows) {
    for (const file of files) {
      const square = squareName(file, rank);
      const piece = gameState.board[rank][file];
      const button = document.createElement("button");
      button.type = "button";
      button.className = `square ${(file + rank) % 2 === 0 ? "light" : "dark"}`;
      button.setAttribute("role", "gridcell");
      button.setAttribute("aria-label", `${square}${piece !== "." ? `, ${pieceName(piece)}` : ""}`);
      button.setAttribute("aria-pressed", String(selectedSquare === square));
      if (selectedSquare === square) button.classList.add("selected");
      if (destinations.includes(square)) {
        button.classList.add("legal");
        if (piece !== ".") button.classList.add("has-piece");
      }
      if (lastMove.includes(square)) button.classList.add("last-move");
      button.addEventListener("click", () => chooseSquare(square));

      if (piece !== ".") {
        const symbol = document.createElement("span");
        symbol.className = `piece ${piece === piece.toUpperCase() ? "white" : "black"}`;
        symbol.setAttribute("aria-hidden", "true");
        symbol.textContent = pieceSymbols[piece];
        button.append(symbol);
      }

      if ((!flipped && file === 0) || (flipped && file === 7)) {
        const label = document.createElement("span");
        label.className = "coordinate rank";
        label.textContent = String(8 - rank);
        button.append(label);
      }
      if ((!flipped && rank === 7) || (flipped && rank === 0)) {
        const label = document.createElement("span");
        label.className = "coordinate file";
        label.textContent = String.fromCharCode(97 + file);
        button.append(label);
      }

      boardElement.append(button);
    }
  }
}

function pieceName(piece) {
  const names = { K: "white king", Q: "white queen", R: "white rook", B: "white bishop", N: "white knight", P: "white pawn",
    k: "black king", q: "black queen", r: "black rook", b: "black bishop", n: "black knight", p: "black pawn" };
  return names[piece];
}

function chooseSquare(square) {
  if (busy || !gameState || gameState.status) return;
  if (selectedSquare && getLegalDestinations(selectedSquare).includes(square)) {
    sendMove(selectedSquare, square);
    return;
  }

  const piece = gameState.board[8 - Number(square[1])][square.charCodeAt(0) - 97];
  const isPlayersPiece = piece !== "." && (piece === piece.toUpperCase()) === (gameState.activeColor === "white");
  selectedSquare = isPlayersPiece ? square : null;
  renderBoard();
}

async function request(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { "Content-Type": "application/json", ...options.headers }
  });
  const result = await response.json();
  if (!response.ok) throw new Error(result.error || "Something went wrong. Please try again.");
  return result;
}

async function startNewGame() {
  setBusy(true);
  messageElement.textContent = "Setting up the board.";
  messageElement.classList.remove("alert");
  selectedSquare = null;
  try {
    const result = await request("/api/games", { method: "POST" });
    gameId = result.gameId;
    localStorage.setItem("little-chess-game", gameId);
    updateGame(result.state);
  } catch (error) {
    showError(error);
  } finally {
    setBusy(false);
  }
}

async function loadGame() {
  try {
    if (!gameId) {
      await startNewGame();
      return;
    }
    updateGame(await request(`/api/games/${gameId}`));
  } catch {
    await startNewGame();
  }
}

async function sendMove(from, to) {
  setBusy(true);
  selectedSquare = null;
  try {
    updateGame(await request(`/api/games/${gameId}/moves`, {
      method: "POST",
      body: JSON.stringify({ from, to })
    }));
  } catch (error) {
    showError(error);
    renderBoard();
  } finally {
    setBusy(false);
  }
}

function updateGame(state) {
  gameState = state;
  legalMoves = state.legalMoves;
  selectedSquare = null;
  const turn = state.activeColor;
  turnLabel.textContent = `${turn[0].toUpperCase()}${turn.slice(1)} to move`;
  turnMark.classList.toggle("black", turn === "black");
  messageElement.classList.remove("alert");
  if (state.status) {
    turnLabel.textContent = "Game over";
    messageElement.textContent = state.status;
  } else if (state.inCheck) {
    messageElement.textContent = `${turn[0].toUpperCase()}${turn.slice(1)} is in check.`;
    messageElement.classList.add("alert");
  } else {
    messageElement.textContent = `${turn === "white" ? "White" : "Black"}’s turn.`;
  }
  renderMoves(state.moves);
  renderBoard();
}

function renderMoves(moves) {
  moveList.replaceChildren();
  for (let index = 0; index < moves.length; index += 2) {
    const item = document.createElement("li");
    item.className = "move-pair";
    const number = document.createElement("span");
    number.className = "move-number";
    number.textContent = `${Math.floor(index / 2) + 1}.`;
    const white = document.createElement("span");
    white.textContent = formatMove(moves[index]);
    item.append(number, white);
    if (moves[index + 1]) {
      const black = document.createElement("span");
      black.textContent = formatMove(moves[index + 1]);
      item.append(black);
    }
    moveList.append(item);
  }
  moveCount.textContent = `${moves.length} ${moves.length === 1 ? "move" : "moves"}`;
  emptyHistory.hidden = moves.length > 0;
}

function formatMove(move) {
  return `${move.slice(0, 2)}–${move.slice(2, 4)}`;
}

function showError(error) {
  messageElement.textContent = error.message;
  messageElement.classList.add("alert");
}

function setBusy(value) {
  busy = value;
  newGameButton.disabled = value;
}

loadGame();
