rootProject.name = "ticket-tool-integrations"

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        mavenCentral()
        // Paper API; it also mirrors Maven Central, which helps when Central rate limits CI.
        maven("https://repo.papermc.io/repository/maven-public/")
    }
}

include("sdk-java", "minecraft")
