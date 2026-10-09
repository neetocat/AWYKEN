import { StatusBar } from 'expo-status-bar';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ScrollView,
  StyleSheet,
  Text,
  View,
  Pressable,
  Linking,
  Alert,
} from 'react-native';

// back end ready variables
interface User {
  firstName: string;
  currentDate: string;
}

interface Weather {
  temperature: number;
  condition: string;
}

interface NextClass {
  courseCode: string;
  title: string;
  startTime: string;
  location: string;
  prepareBy: string;
  leaveBy: string;
  startsIn: string;
}

interface ScheduleItem {
  id: string;
  time: string;
  period: string;
  title: string;
  subtitle: string;
  type: 'in-person' | 'online';
  colorCode: string;
  link?: string;
}

// replace with API calls

const user: User = {
  firstName: 'Mika',
  currentDate: 'Monday, September 16',
};

const weather: Weather = {
  temperature: 29,
  condition: 'Light rain',
};

const nextClass: NextClass = {
  courseCode: 'MATH 1102',
  title: 'Mathematics in the Modern World',
  startTime: '7:30 AM',
  location: 'Room 204 • Main Building',
  prepareBy: '6:15',
  leaveBy: '6:42',
  startsIn: '1h 24m',
};

const schedule: ScheduleItem[] = [
  {
    id: '1',
    time: '10:00',
    period: 'AM',
    title: 'Understanding the Self',
    subtitle: 'Room 302 • Prof. Villanueva',
    type: 'in-person',
    colorCode: '#3B82F6',
  },
  {
    id: '2',
    time: '1:00',
    period: 'PM',
    title: 'Data Structures',
    subtitle: 'Online • Google Meet',
    type: 'online',
    colorCode: '#A855F7',
    link: 'https://meet.google.com/',
  },
];

function SectionTitle({ children }: { children: string }) {
  return <Text style={styles.sectionTitle}>{children}</Text>;
}

export default function Dashboard() {
  const openMeeting = async (link?: string) => {
    if (!link) {
      Alert.alert('Meeting link unavailable', 'No meeting link has been added yet.');
      return;
    }

    try {
      await Linking.openURL(link);
    } catch {
      Alert.alert('Unable to open link', 'Please check the meeting URL.');
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar style="light" />
      <ScrollView
        style={styles.screen}
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.header}>
          <View style={styles.brandMark}>
            <Text style={styles.brandMarkText}>W</Text>
          </View>
          <View style={styles.headerText}>
            <Text style={styles.greeting}>Good morning, {user.firstName}</Text>
            <Text style={styles.mutedText}>{user.currentDate}</Text>
          </View>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Notifications"
            style={styles.notificationButton}
            onPress={() => Alert.alert('Notifications', 'You are all caught up.')}
          >
            <Text style={styles.notificationIcon}>♧</Text>
          </Pressable>
        </View>

        <View style={styles.dayRow}>
          <View style={styles.dayCopy}>
            <Text style={styles.eyebrow}>YOUR DAY</Text>
            <Text style={styles.dayHeadline}>You're all set for a focused day.</Text>
          </View>
          <View style={styles.weather}>
            <Text style={styles.temperature}>{weather.temperature}°</Text>
            <Text style={styles.mutedText}>{weather.condition}</Text>
          </View>
        </View>

        <View style={styles.heroCard}>
          <View style={styles.heroTopRow}>
            <Text style={styles.nextBadge}>NEXT CLASS</Text>
            <Text style={styles.heroMuted}>Starts in {nextClass.startsIn}</Text>
          </View>

          <Text style={styles.courseCode}>{nextClass.courseCode}</Text>
          <Text style={styles.courseTitle}>{nextClass.title}</Text>

          <View style={styles.detailsRow}>
            <Text style={styles.detailText}>◷  {nextClass.startTime}</Text>
            <Text style={styles.detailText}>⌖  {nextClass.location}</Text>
          </View>

          <View style={styles.heroDivider} />

          <View style={styles.preparationRow}>
            <View style={styles.preparationColumn}>
              <Text style={styles.preparationLabel}>PREPARE BY</Text>
              <Text style={styles.preparationTime}>{nextClass.prepareBy} <Text style={styles.amPm}>AM</Text></Text>
            </View>
            <View style={styles.preparationDivider} />
            <View style={styles.preparationColumn}>
              <Text style={styles.preparationLabel}>LEAVE BY</Text>
              <Text style={styles.preparationTime}>{nextClass.leaveBy} <Text style={styles.amPm}>AM</Text></Text>
            </View>
          </View>

          <Pressable
            accessibilityRole="button"
            style={({ pressed }) => [styles.commuteButton, pressed && styles.pressed]}
            onPress={() => Alert.alert('Commute plan', 'Commute planning can be connected here.')}
          >
            <Text style={styles.commuteButtonText}>↝  View commute plan</Text>
          </Pressable>
        </View>

        <View style={styles.scheduleHeader}>
          <View>
            <SectionTitle>Today's schedule</SectionTitle>
            <Text style={styles.mutedText}>{schedule.length} classes</Text>
          </View>
          <Pressable onPress={() => Alert.alert('Schedule', 'Your full schedule can be added here.')}>
            <Text style={styles.seeAll}>See all</Text>
          </Pressable>
        </View>

        <View style={styles.scheduleList}>
          {schedule.map((item) => (
            <View key={item.id} style={styles.scheduleCard}>
              <View style={styles.timeColumn}>
                <Text style={styles.classTime}>{item.time}</Text>
                <Text style={styles.period}>{item.period}</Text>
              </View>
              <View style={[styles.colorBar, { backgroundColor: item.colorCode }]} />
              <View style={styles.classInfo}>
                <Text style={styles.classTitle}>{item.title}</Text>
                <Text style={styles.classSubtitle}>{item.subtitle}</Text>
              </View>
              {item.type === 'online' ? (
                <Pressable
                  style={({ pressed }) => [styles.joinButton, pressed && styles.pressed]}
                  onPress={() => openMeeting(item.link)}
                >
                  <Text style={styles.joinButtonText}>Join</Text>
                </Pressable>
              ) : (
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={`View ${item.title}`}
                  onPress={() => Alert.alert(item.title, item.subtitle)}
                  style={styles.arrowButton}
                >
                  <Text style={styles.arrowText}>›</Text>
                </Pressable>
              )}
            </View>
          ))}
        </View>
      </ScrollView>

      <View style={styles.bottomNav}>
        <Pressable style={styles.navItem} onPress={() => Alert.alert('Home', 'You are on Home.')}>
          <Text style={[styles.navIcon, styles.navActive]}>⌂</Text>
          <Text style={[styles.navLabel, styles.navActive]}>Home</Text>
        </Pressable>
        <Pressable style={styles.navItem} onPress={() => Alert.alert('Schedule', 'Your schedule will appear here.')}>
          <Text style={styles.navIcon}>▦</Text>
          <Text style={styles.navLabel}>Schedule</Text>
        </Pressable>
        <Pressable
          style={styles.addNavItem}
          onPress={() => Alert.alert('Add item', 'Add a class or task here.')}
        >
          <View style={styles.addButton}>
            <Text style={styles.addButtonText}>+</Text>
          </View>
          <Text style={styles.navLabel}>Add</Text>
        </Pressable>
        <Pressable style={styles.navItem} onPress={() => Alert.alert('Alerts', 'No new alerts.')}>
          <Text style={styles.navIcon}>♧</Text>
          <Text style={styles.navLabel}>Alerts</Text>
        </Pressable>
        <Pressable style={styles.navItem} onPress={() => Alert.alert('Profile', 'Profile settings will appear here.')}>
          <Text style={styles.navIcon}>♙</Text>
          <Text style={styles.navLabel}>Profile</Text>
        </Pressable>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#0C1226',
  },
  screen: {
    flex: 1,
    backgroundColor: '#F5F7FA',
  },
  content: {
    paddingHorizontal: 20,
    paddingTop: 16,
    paddingBottom: 28,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 28,
  },
  brandMark: {
    width: 46,
    height: 46,
    borderRadius: 15,
    backgroundColor: '#0C1226',
    alignItems: 'center',
    justifyContent: 'center',
  },
  brandMarkText: {
    color: '#FFFFFF',
    fontSize: 25,
    fontWeight: '800',
  },
  headerText: {
    flex: 1,
    marginLeft: 12,
  },
  greeting: {
    color: '#0C1226',
    fontSize: 17,
    fontWeight: '800',
  },
  mutedText: {
    color: '#9299A8',
    fontSize: 12,
    marginTop: 4,
  },
  notificationButton: {
    width: 40,
    height: 40,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: '#E5E7EB',
    backgroundColor: '#FFFFFF',
    alignItems: 'center',
    justifyContent: 'center',
  },
  notificationIcon: {
    color: '#0C1226',
    fontSize: 22,
  },
  dayRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: 22,
  },
  dayCopy: {
    flex: 1,
    paddingRight: 12,
  },
  eyebrow: {
    color: '#9299A8',
    fontSize: 10,
    fontWeight: '800',
    letterSpacing: 2,
    marginBottom: 8,
  },
  dayHeadline: {
    color: '#0C1226',
    fontSize: 27,
    lineHeight: 33,
    fontWeight: '900',
  },
  weather: {
    alignItems: 'flex-end',
    marginTop: 16,
  },
  temperature: {
    color: '#0C1226',
    fontSize: 28,
    fontWeight: '800',
  },
  heroCard: {
    backgroundColor: '#0C1226',
    borderRadius: 28,
    padding: 22,
    marginBottom: 28,
  },
  heroTopRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 22,
  },
  nextBadge: {
    backgroundColor: '#E5F5EF',
    color: '#0C6B4B',
    overflow: 'hidden',
    borderRadius: 20,
    paddingHorizontal: 12,
    paddingVertical: 6,
    fontSize: 9,
    fontWeight: '900',
    letterSpacing: 1,
  },
  heroMuted: {
    color: '#A7ADBC',
    fontSize: 11,
  },
  courseCode: {
    color: '#A7ADBC',
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 2,
    marginBottom: 6,
  },
  courseTitle: {
    color: '#FFFFFF',
    fontSize: 23,
    lineHeight: 29,
    fontWeight: '800',
    marginBottom: 18,
  },
  detailsRow: {
    gap: 9,
  },
  detailText: {
    color: '#D1D5DB',
    fontSize: 12,
  },
  heroDivider: {
    height: 1,
    backgroundColor: '#303648',
    marginVertical: 20,
  },
  preparationRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 22,
  },
  preparationColumn: {
    flex: 1,
  },
  preparationDivider: {
    width: 1,
    height: 42,
    backgroundColor: '#303648',
    marginHorizontal: 12,
  },
  preparationLabel: {
    color: '#A7ADBC',
    fontSize: 9,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: 8,
  },
  preparationTime: {
    color: '#FFFFFF',
    fontSize: 26,
    fontWeight: '800',
  },
  amPm: {
    fontSize: 12,
    fontWeight: '500',
  },
  commuteButton: {
    backgroundColor: '#FFFFFF',
    borderRadius: 15,
    paddingVertical: 14,
    alignItems: 'center',
  },
  commuteButtonText: {
    color: '#0C1226',
    fontWeight: '800',
    fontSize: 13,
  },
  pressed: {
    opacity: 0.7,
  },
  scheduleHeader: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    marginBottom: 14,
  },
  sectionTitle: {
    color: '#0C1226',
    fontSize: 18,
    fontWeight: '800',
    marginBottom: 3,
  },
  seeAll: {
    color: '#0C1226',
    fontSize: 12,
    fontWeight: '800',
    paddingBottom: 3,
  },
  scheduleList: {
    gap: 10,
  },
  scheduleCard: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: '#FFFFFF',
    borderRadius: 17,
    borderWidth: 1,
    borderColor: '#ECEEF2',
    padding: 13,
    minHeight: 78,
  },
  timeColumn: {
    width: 52,
    alignItems: 'center',
  },
  classTime: {
    color: '#0C1226',
    fontSize: 12,
    fontWeight: '800',
  },
  period: {
    color: '#9299A8',
    fontSize: 9,
    marginTop: 3,
  },
  colorBar: {
    width: 4,
    height: 38,
    borderRadius: 4,
    marginHorizontal: 12,
  },
  classInfo: {
    flex: 1,
    paddingRight: 6,
  },
  classTitle: {
    color: '#0C1226',
    fontSize: 12,
    fontWeight: '800',
    marginBottom: 5,
  },
  classSubtitle: {
    color: '#9299A8',
    fontSize: 10,
  },
  joinButton: {
    borderWidth: 1,
    borderColor: '#E5E7EB',
    borderRadius: 10,
    paddingHorizontal: 10,
    paddingVertical: 7,
  },
  joinButtonText: {
    color: '#0C1226',
    fontSize: 11,
    fontWeight: '800',
  },
  arrowButton: {
    width: 30,
    alignItems: 'center',
  },
  arrowText: {
    color: '#9299A8',
    fontSize: 26,
  },
  bottomNav: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-around',
    backgroundColor: '#FFFFFF',
    borderTopWidth: 1,
    borderTopColor: '#ECEEF2',
    paddingTop: 10,
    paddingBottom: 8,
    paddingHorizontal: 6,
  },
  navItem: {
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: 3,
  },
  navIcon: {
    color: '#9299A8',
    fontSize: 22,
    height: 25,
  },
  navActive: {
    color: '#0C1226',
    fontWeight: '800',
  },
  navLabel: {
    color: '#9299A8',
    fontSize: 9,
  },
  addNavItem: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: -20,
    gap: 2,
  },
  addButton: {
    width: 48,
    height: 48,
    borderRadius: 24,
    backgroundColor: '#0C1226',
    alignItems: 'center',
    justifyContent: 'center',
  },
  addButtonText: {
    color: '#FFFFFF',
    fontSize: 30,
    lineHeight: 34,
  },
});
