module a__helper() cube(1);
module a__foo() a__helper();
module b__helper() sphere(1);
module b__bar() b__helper();
a__foo();
b__bar();
