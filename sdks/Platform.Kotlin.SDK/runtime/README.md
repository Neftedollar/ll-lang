# Platform.Kotlin.SDK runtime

Compile generated `.kt` files with `kotlinc`:

```
kotlinc Main.kt -include-runtime -d Main.jar
java -jar Main.jar
```

For multi-file projects, use Gradle with `kotlin("jvm")` plugin.
