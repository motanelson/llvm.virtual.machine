; Branching and print demonstration
define i32 @main() {
entry:
  %val = add i32 40, 2
  call void @print(i32 %val)
  %is_42 = icmp eq i32 %val, 42
  br i1 %is_42, label %success, label %failure

success:
  call void @print(i32 1000)
  ret i32 0

failure:
  call void @print(i32 -1)
  ret i32 1
}