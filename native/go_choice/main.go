package main

/*
#include <stdint.h>
*/
import "C"
import (
    "math"
    "unsafe"
)

//export enorys_argmax_go
func enorys_argmax_go(values *C.double, count C.int) C.int {
    n := int(count)
    if values == nil || n <= 0 || n > 1024 { return -1 }
    scores := unsafe.Slice((*float64)(unsafe.Pointer(values)), n)
    winner := 0
    best := scores[0]
    if math.IsNaN(best) || math.IsInf(best, 0) { return -1 }
    for i := 1; i < n; i++ {
        x := scores[i]
        if math.IsNaN(x) || math.IsInf(x, 0) { return -1 }
        if x > best { best = x; winner = i }
    }
    return C.int(winner)
}
func main() {}
