#include "enorys_math.h"
#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

int enorys_noisy_or(const EnorysCause* causes, int count, double leak,
                    double* output, double* diagnostics) {
    if (!causes || !output || !diagnostics || count < 0 || count > 8 ||
        !isfinite(leak) || leak < 0 || leak > 1) return 1;
    for (int i=0; i<count; i++) {
        EnorysCause c=causes[i];
        if (!isfinite(c.prior) || c.prior<0 || c.prior>1 ||
            !isfinite(c.strength) || c.strength<0 || c.strength>1 ||
            !isfinite(c.likelihood_ratio) || c.likelihood_ratio<=0) return 1;
    }
    int states=1 << count;
    double weights[256], logpriors[256], maximum=-INFINITY;
    for(int mask=0;mask<states;mask++) {
        double logp=0, fail=1-leak;
        int possible=1;
        for(int i=0;i<count;i++) {
            EnorysCause c=causes[i];
            int on=(mask & (1<<i))!=0;
            double p=on?c.prior:1-c.prior;
            if(p==0){possible=0;break;}
            logp+=log(p);
            if(on){logp+=log(c.likelihood_ratio);fail*=1-c.strength;}
        }
        logpriors[mask]=possible?logp:-INFINITY;
        double effect=1-fail;
        weights[mask]=possible&&effect>0?logp+log(effect):-INFINITY;
        if(weights[mask]>maximum)maximum=weights[mask];
    }
    if(isinf(maximum)&&maximum<0)return 2;
    double denominator=0,no_cause=0,leak_mass=0;
    for(int i=0;i<count;i++)output[i]=0;
    for(int mask=0;mask<states;mask++) {
        double scaled=isinf(weights[mask])&&weights[mask]<0?0:exp(weights[mask]-maximum);
        denominator+=scaled;
        if(mask==0)no_cause+=scaled;
        for(int i=0;i<count;i++)if(mask&(1<<i))output[i]+=scaled;
        if(leak>0&&isfinite(logpriors[mask]))
            leak_mass+=exp(logpriors[mask]+log(leak)-maximum);
    }
    for(int i=0;i<count;i++)output[i]/=denominator;
    diagnostics[0]=no_cause/denominator;
    diagnostics[1]=leak_mass/denominator;
    diagnostics[2]=maximum+log(denominator);
    return 0;
}

int enorys_noisy_or_batch(const EnorysCause* causes, int count,
                          const double* leaks, int batch_size,
                          double* outputs, double* diagnostics) {
    if(!causes||!leaks||!outputs||!diagnostics||count<1||count>8||
       batch_size<0||batch_size>10000000)return 1;
    for(int i=0;i<batch_size;i++) {
        int result=enorys_noisy_or(causes+(size_t)i*count,count,leaks[i],
                                   outputs+(size_t)i*count,diagnostics+(size_t)i*3);
        if(result)return result;
    }
    return 0;
}
