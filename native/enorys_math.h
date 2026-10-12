#ifndef ENORYS_MATH_H
#define ENORYS_MATH_H
#ifdef __cplusplus
extern "C" {
#endif
typedef struct { double prior, strength, likelihood_ratio; } EnorysCause;
/* posterior_out[count], diagnostics_out[3] = no listed cause, leak active, log P(E, evidence). */
int enorys_noisy_or(const EnorysCause* causes, int count, double leak,
                    double* posterior_out, double* diagnostics_out);
/* Packed causes: batch_size * count. Output: batch_size * count and batch_size * 3.
   Returns zero on success or nonzero error code. */
int enorys_noisy_or_batch(const EnorysCause* causes, int count,
                          const double* leaks, int batch_size,
                          double* posteriors_out, double* diagnostics_out);
#ifdef __cplusplus
}
#endif
#endif
