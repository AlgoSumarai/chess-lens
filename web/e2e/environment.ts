// QEMU on an x86 host is compatibility evidence, not native ARM performance.
// Production job deadlines and ordinary browser thresholds stay unchanged.
export const browserTimeoutScale = process.env.CHESSLENS_EMULATED_ARCH === 'arm64' ? 6 : 1;
export const browserTimeout = (milliseconds: number) => milliseconds * browserTimeoutScale;
