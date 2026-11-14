; Recursive Factorial in LLVM IR
define i32 @factorial(i32 %n) {
entry:
  %cmp = icmp sle i32 %n, 1
  br i1 %cmp, label %base, label %recurse

base:
  ret i32 1

recurse:
  %n_sub = sub i32 %n, 1
  %call_prev = call i32 @factorial(i32 %n_sub)
  %result = mul i32 %n, %call_prev
  ret i32 %result
}

define i32 @main() {
entry:
  ; Calculate factorial of 5 (5! = 120)
  %res = call i32 @factorial(i32 5)
  call void @print(i32 %res)
  ret i32 0
}