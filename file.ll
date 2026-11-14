; File without a standard @main function
define i32 @calculate_sum() {
entry:
  %x = add i32 100, 250
  call void @print(i32 %x)
  ret i32 %x
}