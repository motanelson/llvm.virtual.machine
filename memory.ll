; Fibonacci using memory pointers
define i32 @main() {
entry:
  %a = alloca i32
  %b = alloca i32
  %i = alloca i32
  store i32 0, ptr %a
  store i32 1, ptr %b
  store i32 0, ptr %i
  br label %loop

loop:
  %cur_i = load i32, ptr %i
  %cond = icmp slt i32 %cur_i, 8
  br i1 %cond, label %body, label %done

body:
  %val_a = load i32, ptr %a
  call void @print(i32 %val_a)
  %val_b = load i32, ptr %b
  %next = add i32 %val_a, %val_b
  store i32 %val_b, ptr %a
  store i32 %next, ptr %b
  %next_i = add i32 %cur_i, 1
  store i32 %next_i, ptr %i
  br label %loop

done:
  ret i32 0
}