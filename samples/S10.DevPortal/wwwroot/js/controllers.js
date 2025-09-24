// AngularJS Controllers for S10 DevPortal

angular.module('DevPortalApp')
    .controller('DemoController', ['$scope', '$rootScope', function($scope, $rootScope) {
        $scope.loading = false;
        $scope.capabilities = null;
        $scope.performanceResults = null;
        $scope.bulkResults = null;
        $scope.relationshipDemo = null;
        $scope.setRoutingDemo = null;

        // Demo scenarios
        $scope.scenarios = DevPortalDemo.getDemoScenarios();

        // Initialize
        $scope.init = async function() {
            try {
                $scope.capabilities = await DevPortalDemo.displayProviderCapabilities();
                $scope.$apply();
            } catch (error) {
                console.error('Failed to load initial capabilities:', error);
            }
        };

        // Provider switching
        $scope.switchProvider = async function(provider) {
            $scope.loading = true;
            try {
                await DevPortalDemo.switchProvider(provider);
                $scope.capabilities = await DevPortalDemo.displayProviderCapabilities();
                $rootScope.updateProviderIndicator(provider);
            } catch (error) {
                console.error('Provider switch failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Performance comparison
        $scope.runPerformanceComparison = async function() {
            $scope.loading = true;
            try {
                $scope.performanceResults = await DevPortalDemo.runPerformanceComparison();
            } catch (error) {
                console.error('Performance comparison failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Bulk operations demo
        $scope.runBulkDemo = async function(count) {
            $scope.loading = true;
            try {
                $scope.bulkResults = await DevPortalDemo.demonstrateBulkOperations(count || 100);
            } catch (error) {
                console.error('Bulk demo failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Relationship navigation demo
        $scope.runRelationshipDemo = async function() {
            $scope.loading = true;
            try {
                $scope.relationshipDemo = await DevPortalDemo.demonstrateRelationshipNavigation();
            } catch (error) {
                console.error('Relationship demo failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Set routing demo
        $scope.runSetRoutingDemo = async function() {
            $scope.loading = true;
            try {
                $scope.setRoutingDemo = await DevPortalDemo.demonstrateSetRouting();
            } catch (error) {
                console.error('Set routing demo failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Data management
        $scope.seedDemoData = async function() {
            $scope.loading = true;
            try {
                await DevPortalDemo.seedDemoData();
                // Refresh capabilities after seeding
                $scope.capabilities = await DevPortalDemo.displayProviderCapabilities();
            } catch (error) {
                console.error('Seed demo failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        $scope.clearDemoData = async function() {
            $scope.loading = true;
            try {
                await DevPortalDemo.clearDemoData();
                // Clear local demo results
                $scope.performanceResults = null;
                $scope.bulkResults = null;
                $scope.relationshipDemo = null;
                $scope.setRoutingDemo = null;
            } catch (error) {
                console.error('Clear demo failed:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Utility functions
        $scope.formatTiming = DevPortalDemo.formatTiming;
        $scope.formatCapabilities = DevPortalDemo.formatCapabilities;

        // Initialize on load
        $scope.init();
    }])

    .controller('ArticlesController', ['$scope', function($scope) {
        $scope.articles = [];
        $scope.loading = false;
        $scope.currentSet = 'all';
        $scope.newArticle = {};

        $scope.loadArticles = async function(set = null) {
            $scope.loading = true;
            try {
                $scope.articles = await DevPortalApi.getArticles(set);
                $scope.currentSet = set || 'all';
            } catch (error) {
                console.error('Failed to load articles:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        $scope.createArticle = async function() {
            if (!$scope.newArticle.title) return;

            $scope.loading = true;
            try {
                await DevPortalApi.createArticle($scope.newArticle);
                $scope.newArticle = {};
                await $scope.loadArticles($scope.currentSet);
            } catch (error) {
                console.error('Failed to create article:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        $scope.deleteArticle = async function(id) {
            if (!confirm('Delete this article?')) return;

            $scope.loading = true;
            try {
                await DevPortalApi.deleteArticle(id);
                await $scope.loadArticles($scope.currentSet);
            } catch (error) {
                console.error('Failed to delete article:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Initialize
        $scope.loadArticles();
    }])

    .controller('TechnologiesController', ['$scope', function($scope) {
        $scope.technologies = [];
        $scope.selectedTechnology = null;
        $scope.hierarchy = null;
        $scope.loading = false;
        $scope.newTechnology = {};

        $scope.loadTechnologies = async function() {
            $scope.loading = true;
            try {
                $scope.technologies = await DevPortalApi.getTechnologies();
            } catch (error) {
                console.error('Failed to load technologies:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        $scope.selectTechnology = async function(technology) {
            $scope.selectedTechnology = technology;
            $scope.loading = true;
            try {
                $scope.hierarchy = await DevPortalApi.getTechnologyHierarchy(technology.id);
            } catch (error) {
                console.error('Failed to load technology hierarchy:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        $scope.createTechnology = async function() {
            if (!$scope.newTechnology.name) return;

            $scope.loading = true;
            try {
                await DevPortalApi.createTechnology($scope.newTechnology);
                $scope.newTechnology = {};
                await $scope.loadTechnologies();
            } catch (error) {
                console.error('Failed to create technology:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Initialize
        $scope.loadTechnologies();
    }])

    .controller('UsersController', ['$scope', function($scope) {
        $scope.users = [];
        $scope.loading = false;
        $scope.newUser = {};

        $scope.loadUsers = async function() {
            $scope.loading = true;
            try {
                $scope.users = await DevPortalApi.getUsers();
            } catch (error) {
                console.error('Failed to load users:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        $scope.createUser = async function() {
            if (!$scope.newUser.username) return;

            $scope.loading = true;
            try {
                await DevPortalApi.createUser($scope.newUser);
                $scope.newUser = {};
                await $scope.loadUsers();
            } catch (error) {
                console.error('Failed to create user:', error);
            } finally {
                $scope.loading = false;
                $scope.$apply();
            }
        };

        // Initialize
        $scope.loadUsers();
    }]);