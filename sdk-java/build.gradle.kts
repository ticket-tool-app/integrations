plugins {
    `java-library`
}

base {
    archivesName.set("ticket-tool-plugin-sdk")
}


java {
    withSourcesJar()
}

dependencies {
    testImplementation(platform("org.junit:junit-bom:5.11.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

// The vectors live in spec/ so every SDK reads the same file.
sourceSets.test {
    resources.srcDir(rootProject.file("spec/test-vectors"))
}
