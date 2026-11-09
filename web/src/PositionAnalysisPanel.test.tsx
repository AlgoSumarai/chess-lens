import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { Chess } from 'chess.js';
import { PositionAnalysisPanel } from './PositionAnalysisPanel';
import { LiveEvaluationBar } from './LiveEvaluationBar';
import { usePositionAnalysis, type PositionJob } from './positionAnalysis';
import { api, send } from './api';

vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
afterEach(() => { cleanup(); vi.useRealTimers(); vi.clearAllMocks(); });
const start = new Chess().fen();
const e4Board = new Chess(); e4Board.move('e4');
const afterE4 = e4Board.fen();
const play = vi.fn(() => true);
function Harness({ fen = start, ply = 0, ready = true, orientation = 'white' }: { fen?: string; ply?: number; ready?: boolean; orientation?: 'white' | 'black' }) {
  const analysis = usePositionAnalysis({ gameId: 'game', ply, variation: [], fen, ready,
    legalMoves: new Chess(fen).moves({ verbose: true }).map(m => m.lan), revision: 0 });
  return <><LiveEvaluationBar analysis={analysis} orientation={orientation} /><PositionAnalysisPanel analysis={analysis} fen={fen} onPlay={play} /></>;
}
function requests() {
  vi.useFakeTimers();
  const pending: { sequence: number; resolve: (value: PositionJob) => void; reject: (reason: Error) => void }[] = [];
  vi.mocked(send).mockImplementation((_path, data) => {
    const input = data as { cancelOnly?: boolean; sequence: number };
    return input.cancelOnly ? Promise.resolve(undefined) : new Promise((resolve, reject) => pending.push({ sequence: input.sequence, resolve, reject }));
  });
  vi.mocked(api).mockImplementation(() => new Promise(() => {}));
  return pending;
}
function answer(sequence: number, fen: string, moves: string[], score = 40): PositionJob {
  return { id: `job-${sequence}`, fen, sequence, state: 'completed', version: 3, error: null,
    result: { engine: 'Labelled test fixture', lines: [{ rank: 1, depth: 10, score: { kind: 'cp', value: score }, pv: moves, bound: false }] } };
}
async function debounce() { await act(async () => { await vi.advanceTimersByTimeAsync(350); }); }

test('late replies, return to the same position, pending state, orientation and stopping keep the live score synchronized', async () => {
  const pending = requests();
  const view = render(<Harness />);
  expect(screen.getByRole('img')).toHaveAccessibleName('Position analysis is off');
  expect(document.querySelector('.live-evaluation-score')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await debounce();
  view.rerender(<Harness ply={1} fen={afterE4} />);
  expect(screen.getByLabelText('Candidate continuations')).toBeEmptyDOMElement();
  expect(screen.getByRole('img')).toHaveAttribute('aria-busy', 'true');
  await debounce();
  await act(async () => { pending[0].resolve(answer(pending[0].sequence, start, ['d2d4'])); });
  expect(screen.queryByRole('button', { name: /Play candidate/ })).toBeNull();
  await act(async () => { pending[1].resolve(answer(pending[1].sequence, afterE4, ['e7e5'])); });
  expect(screen.getByRole('img')).toHaveAccessibleName('White’s evaluation: -0.4');
  expect(screen.getByLabelText('Candidate continuations')).toHaveTextContent('for Black');
  fireEvent.click(screen.getByRole('button', { name: 'Play candidate 1: e5' }));
  expect(play).toHaveBeenCalledWith('e7e5');
  view.rerender(<Harness ply={1} fen={afterE4} orientation="black" />);
  expect(screen.getByRole('img')).toHaveClass('black');
  expect(screen.getByRole('img')).toHaveAccessibleName('White’s evaluation: -0.4');
  view.rerender(<Harness />);
  expect(screen.getByRole('img')).toHaveAttribute('aria-busy', 'true');
  expect(screen.getByLabelText('Candidate continuations')).toBeEmptyDOMElement();
  await debounce();
  await act(async () => { pending[2].resolve(answer(pending[2].sequence, start, ['g1f3'])); });
  expect(screen.getByRole('button', { name: 'Play candidate 1: Nf3' })).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Stop position analysis' }));
  expect(document.querySelector('.live-evaluation-score')).toBeNull();
  expect(screen.queryByLabelText('Candidate continuations')).toBeNull();
  expect(vi.mocked(send).mock.calls.some(([, data]) => (data as { cancelOnly?: boolean }).cancelOnly)).toBe(true);
});

test('an old poll cannot replace the result of a newer position or show a score after stopping', async () => {
  const pending = requests();
  let finishPoll!: (job: PositionJob) => void;
  vi.mocked(api).mockImplementation(() => new Promise(resolve => { finishPoll = resolve; }));
  const view = render(<Harness />);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await debounce();
  await act(async () => { pending[0].resolve({ ...answer(pending[0].sequence, start, []), state: 'running', result: null }); });
  view.rerender(<Harness ply={1} fen={afterE4} />);
  await debounce();
  await act(async () => { pending[1].resolve(answer(pending[1].sequence, afterE4, ['e7e5'], -90)); });
  await act(async () => { finishPoll(answer(pending[0].sequence, start, ['d2d4'], 500)); });
  expect(screen.getByRole('img')).toHaveAccessibleName('White’s evaluation: +0.9');
  expect(screen.getByRole('button', { name: 'Play candidate 1: e5' })).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Stop position analysis' }));
  expect(document.querySelector('.live-evaluation-score')).toBeNull();
});

test.each([
  ['7k/6Q1/6K1/8/8/8/8/8 b - - 0 1', 'checkmate'],
  ['7k/5Q2/6K1/8/8/8/8/8 b - - 0 1', 'stalemate'],
])('terminal %s has an empty candidate list without queuing an engine job (%s)', async (fen) => {
  requests();
  render(<Harness fen={fen} />);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await debounce();
  expect(screen.getByRole('status', { name: 'Position analysis status' })).toHaveTextContent('No legal moves');
  expect(screen.getByLabelText('Candidate continuations')).toBeEmptyDOMElement();
  expect(send).not.toHaveBeenCalled();
});

test('candidates are ranked, limited to three and formatted as at most ten SAN half-moves', async () => {
  const pending = requests();
  render(<Harness />);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await debounce();
  const board = new Chess();
  const pv = ['e4', 'e5', 'Nf3', 'Nc6', 'Bc4', 'Nf6', 'O-O', 'Be7', 'd3', 'O-O', 'Nc3', 'd6'].map(san => board.move(san).lan);
  const job = answer(pending[0].sequence, start, pv);
  job.result!.lines = [4, 2, 1, 3].map(rank => ({ ...job.result!.lines[0], rank }));
  await act(async () => { pending[0].resolve(job); });
  const buttons = screen.getAllByRole('button', { name: /Play candidate/ });
  expect(buttons).toHaveLength(3);
  expect(buttons.map(button => button.getAttribute('aria-label'))).toEqual([1, 2, 3].map(rank => `Play candidate ${rank}: e4`));
  for (const button of buttons) expect(button.querySelector('.variation-line')).toHaveTextContent('e4 e5 Nf3 Nc6 Bc4 Nf6 O-O Be7 d3 O-O');
  expect(screen.getByLabelText('Candidate continuations')).not.toHaveTextContent('Nc3');
});

test('promotion, check and mate use SAN, with mate evaluation kept distinct', async () => {
  const pending = requests();
  const fen = '7k/P7/6K1/8/8/8/8/8 w - - 0 1';
  render(<Harness fen={fen} />);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await debounce();
  const job = answer(pending[0].sequence, fen, ['a7a8q']);
  job.result!.lines[0].score = { kind: 'mate', value: 2 };
  await act(async () => { pending[0].resolve(job); });
  expect(screen.getByRole('button', { name: 'Play candidate 1: a8=Q#' })).toBeVisible();
  expect(screen.getByRole('img')).toHaveAccessibleName('White’s evaluation: M2');
});


test('stopping an in-flight request hides its late result and enabling again starts pending', async () => {
  const pending = requests();
  render(<Harness />);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await debounce();
  fireEvent.click(screen.getByRole('button', { name: 'Stop position analysis' }));
  await act(async () => { pending[0].resolve(answer(pending[0].sequence, start, ['e2e4'])); });
  expect(document.querySelector('.live-evaluation-score')).toBeNull();
  expect(screen.queryByLabelText('Candidate continuations')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  expect(screen.getByRole('img')).toHaveAttribute('aria-busy', 'true');
  expect(screen.getByLabelText('Candidate continuations')).toBeEmptyDOMElement();
  await debounce();
  expect(pending).toHaveLength(2);
});
