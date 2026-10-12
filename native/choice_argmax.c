#include <math.h>
#include <stddef.h>
/* Strict > preserves the first index when scores tie, matching C# choice. */
int enorys_argmax_c(const double *values, int count) {
    if(values==NULL || count<=0 || count>1024)return -1;
    double best=values[0];int winner=0;
    if(!isfinite(best))return -1;
    for(int i=1;i<count;i++){
        if(!isfinite(values[i]))return -1;
        if(values[i]>best){best=values[i];winner=i;}
    }
    return winner;
}
