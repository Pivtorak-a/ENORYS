const std = @import("std");
export fn enorys_argmax_zig(values: ?[*]const f64, count: c_int) c_int {
    if (values == null or count <= 0 or count > 1024) return -1;
    const data = values.?;
    var best = data[0];
    if (!std.math.isFinite(best)) return -1;
    var winner: c_int = 0;
    var i: usize = 1;
    const n: usize = @intCast(count);
    while (i < n) : (i += 1) {
        const value = data[i];
        if (!std.math.isFinite(value)) return -1;
        if (value > best) {
            best = value;
            winner = @intCast(i);
        }
    }
    return winner;
}
