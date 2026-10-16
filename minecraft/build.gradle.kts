plugins {
    java
}

base {
    archivesName.set("TicketToolLink")
}


dependencies {
    compileOnly("io.papermc.paper:paper-api:1.20.6-R0.1-SNAPSHOT")
    implementation(project(":sdk-java"))

    testImplementation(platform("org.junit:junit-bom:5.11.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
    testImplementation("io.papermc.paper:paper-api:1.20.6-R0.1-SNAPSHOT")
}

tasks.processResources {
    val props = mapOf("version" to project.version)
    inputs.properties(props)
    filesMatching("plugin.yml") {
        expand(props)
    }
}

// The SDK has no dependencies of its own, so its classes go in as they are:
// nothing third-party to relocate.
tasks.jar {
    dependsOn(":sdk-java:jar")
    from(project(":sdk-java").sourceSets.main.get().output)
    duplicatesStrategy = DuplicatesStrategy.EXCLUDE
}
