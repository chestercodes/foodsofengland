---
agent: speckit.plan
---

the code needs to be written in f# and it should be a console application running on dotnet 10

it should save the data to sqlite, using ef core as an orm

it should have automated tests to future proof the code and ensure that it works as expected, each step should have its own tests, and there should be integration tests to ensure that the entire pipeline works together correctly

the code should be strongly typed where possible, the main exception will be where it gets data in and out of ef core, where it should use the primitive types that ef core supports, but the rest of the code should use domain-specific types to ensure that the code is as clear and maintainable as possible

comments should be kept to a minimum, and the code should be self-explanatory where possible, but where comments are necessary they should be clear and concise, and they should explain the why behind the code rather than the what

